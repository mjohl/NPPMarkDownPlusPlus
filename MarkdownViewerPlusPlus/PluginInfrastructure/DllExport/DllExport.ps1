<#
.SYNOPSIS
    Adds the unmanaged exports Notepad++ needs to the (merged) plugin assembly.

.DESCRIPTION
    Drives the NppPlugin.DllExport weaver (ildasm -> rewrite IL -> ilasm) directly. The original
    MSBuild task (NppPlugin.DllExport.MSBuild.dll) was built against MSBuild 2.0 and can no longer be
    loaded by current MSBuild versions, so the project runs this script after the build instead.

.EXAMPLE
    .\DllExport.ps1 -InputFile bin\x64\Release\MarkdownViewerPlusPlus.dll -Platform x64
#>
param(
    [Parameter(Mandatory)] [string] $InputFile,
    [Parameter(Mandatory)] [ValidateSet('x86', 'x64')] [string] $Platform,
    # Folder containing ildasm.exe (MSBuild: $(SDK40ToolsPath)); searched for when empty
    [string] $SdkPath = '',
    # Folder containing ilasm.exe; defaults to the running .NET Framework directory
    [string] $FrameworkPath = '',
    # Folder containing NppPlugin.DllExport.dll and Mono.Cecil.dll; defaults to this script's folder
    [string] $DllExportDir = '',
    # Keep the temporary IL files for troubleshooting
    [switch] $KeepIntermediateFiles
)
$ErrorActionPreference = 'Stop'
# $PSScriptRoot is not available in parameter defaults of Windows PowerShell 5.1 scripts run with -File
if (-not $DllExportDir) { $DllExportDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

function Find-IlDasm {
    if ($SdkPath -and (Test-Path (Join-Path $SdkPath 'ildasm.exe'))) { return $SdkPath }
    $candidates = Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows\*\bin\NETFX * Tools\ildasm.exe" -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Directory.Name -replace '^NETFX ([\d.]+) Tools$', '$1') } -Descending
    if (-not $candidates) { throw 'ildasm.exe not found. Install the .NET Framework 4.8 Developer Pack / Windows SDK.' }
    return $candidates[0].DirectoryName
}

if (-not $FrameworkPath) { $FrameworkPath = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory() }
if (-not (Test-Path (Join-Path $FrameworkPath 'ilasm.exe'))) { throw "ilasm.exe not found in '$FrameworkPath'." }
$SdkPath = Find-IlDasm

[Reflection.Assembly]::LoadFrom((Join-Path $DllExportDir 'Mono.Cecil.dll')) | Out-Null
$asm = [Reflection.Assembly]::LoadFrom((Join-Path $DllExportDir 'NppPlugin.DllExport.dll'))
$bf = [Reflection.BindingFlags]'Instance,Public,NonPublic'

# The weaver's tool objects dispose their service provider chain, which would clear a ServiceContainer and
# make later notifier lookups return null - so use a minimal, non-disposable provider.
if (-not ('DllExportServiceProvider' -as [type])) {
    Add-Type -TypeDefinition @'
public class DllExportServiceProvider : System.IServiceProvider {
    public System.Type ServiceType; public object Service;
    public object GetService(System.Type t) { return t == ServiceType ? Service : null; }
}
'@
}
$notifier = [Activator]::CreateInstance($asm.GetType('NppPlugin.DllExport.DllExportNotifier'))
$notifier.add_Notification({ param($s, $e) if ($e.Severity -ge 1) { Write-Host ("DllExport [{0}] {1}: {2}" -f $e.Severity, $e.Code, $e.Message) } })
$provider = New-Object DllExportServiceProvider
$provider.ServiceType = $asm.GetType('NppPlugin.DllExport.IDllExportNotifier')
$provider.Service = $notifier

$inputs = [Activator]::CreateInstance($asm.GetType('NppPlugin.DllExport.InputValuesCore'), @([IServiceProvider]$provider))
$inputs.InputFileName = (Resolve-Path $InputFile).Path
$inputs.Cpu = [Enum]::Parse($asm.GetType('NppPlugin.DllExport.CpuPlatform'), $Platform, $true)
$inputs.FrameworkPath = $FrameworkPath
$inputs.SdkPath = $SdkPath
$inputs.LibToolPath = ''
$inputs.EmitDebugSymbols = $false
$inputs.LeaveIntermediateFiles = 'false'
$inputs.DllExportAttributeFullName = 'NppPlugin.DllExport.DllExportAttribute'
$inputs.InferOutputFile()

$weaver = [Activator]::CreateInstance($asm.GetType('NppPlugin.DllExport.DllExportWeaver'), @([IServiceProvider]$provider))
$weaver.InputValues = $inputs
$weaver.Timeout = 120000

$inspectorType = $asm.GetType('NppPlugin.DllExport.ExportAssemblyInspector')
$inspector = $inspectorType.GetConstructors()[0].Invoke(@($inputs))
$exports = $inspectorType.GetMethod('ExtractExports', [Type[]]@()).Invoke($inspector, @())
if ($exports.Count -eq 0) { throw "No [DllExport] methods found in $InputFile" }
$weaver.GetType().GetProperty('Exports', $bf).SetValue($weaver, $exports, $null)

# The ildasm output of current compilers contains constructs the .NET Framework 4 ilasm cannot read back
if (-not ('DllExportIlPatcher' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class DllExportIlPatcher
{
    // Floating point constants printed as text (-nan(ind), inf, -inf) -> raw little-endian bytes
    static readonly Regex FloatLiteral = new Regex(@"^(\s*IL_[0-9a-f]+:\s+ldc\.r(?<size>[48])\s+)(?<value>-?(?:nan(?:\(ind\))?|inf))\s*$", RegexOptions.Compiled);
    // The parameter name at the end of a parameter declaration
    static readonly Regex TrailingName = new Regex(@"'(?<name>[^']+)'(?<tail>\s*)$", RegexOptions.Compiled);
    static readonly Regex QuotedName = new Regex(@"'(?<name>[^']+)'", RegexOptions.Compiled);
    // .param type 'T' (generic parameter attributes, e.g. nullable annotations) -> .param type [index]
    static readonly Regex ParamType = new Regex(@"^(?<indent>\s*)\.param type '(?<name>[^']+)'\s*$", RegexOptions.Compiled);

    public static string Patch(string il, out string summary)
    {
        string[] lines = il.Split('\n');
        var output = new StringBuilder(il.Length + 4096);
        var classGenerics = new Stack<string[]>();
        string[] methodGenerics = null;
        StringBuilder header = null;
        bool headerIsMethod = false, headerIsExtern = false;
        int floats = 0, duplicates = 0, paramTypes = 0, unknownOwners = 0, forwarders = 0;
        bool inForwarder = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.TrimStart();

            // Type forwarders merged from facades (e.g. System.Numerics.Vectors -> System.Numerics) are not needed
            // in the plugin, and the weaver drops the assembly references they point to
            if (inForwarder)
            {
                if (trimmed.StartsWith("}")) inForwarder = false;
                continue;
            }
            if (header == null && trimmed.StartsWith(".class extern forwarder "))
            {
                inForwarder = true;
                forwarders++;
                continue;
            }

            if (header == null && (trimmed.StartsWith(".method ") || trimmed.StartsWith(".class ")))
            {
                header = new StringBuilder();
                headerIsMethod = trimmed.StartsWith(".method ");
                headerIsExtern = trimmed.StartsWith(".class extern ");
            }

            if (header != null)
            {
                if (trimmed.StartsWith("{"))
                {
                    string text = header.ToString();
                    if (headerIsMethod)
                    {
                        methodGenerics = GenericParameterNames(text, true);
                        text = RenameDuplicateParameters(text, ref duplicates);
                    }
                    else if (!headerIsExtern)
                    {
                        string declaration = Regex.Split(text, @"\s(?:extends|implements)\s")[0];
                        classGenerics.Push(GenericParameterNames(declaration, false));
                    }
                    output.Append(text);
                    header = null;
                }
                else
                {
                    header.Append(line).Append("\r\n");
                    continue;
                }
            }

            // Attributes on metadata ildasm 4.x cannot express (e.g. nullable annotations of generic constraints)
            if (trimmed.StartsWith(".custom (UNKNOWN_OWNER)")) { unknownOwners++; continue; }

            if (trimmed.StartsWith("} // end of method")) methodGenerics = null;
            else if (trimmed.StartsWith("} // end of class") && classGenerics.Count > 0) classGenerics.Pop();

            Match floatLiteral = FloatLiteral.Match(line);
            if (floatLiteral.Success)
            {
                line = floatLiteral.Groups[1].Value + FloatBytes(floatLiteral.Groups["size"].Value == "8", floatLiteral.Groups["value"].Value);
                floats++;
            }

            Match paramType = ParamType.Match(line);
            if (paramType.Success)
            {
                string[] scope = methodGenerics ?? (classGenerics.Count > 0 ? classGenerics.Peek() : new string[0]);
                int index = Array.IndexOf(scope, paramType.Groups["name"].Value);
                if (index < 0) throw new InvalidOperationException("Unknown generic parameter in: " + line.Trim());
                line = paramType.Groups["indent"].Value + ".param type [" + (index + 1) + "]";
                paramTypes++;
            }

            output.Append(line).Append("\r\n");
        }

        summary = string.Format("{0} float constants, {1} duplicate parameter names, {2} generic parameter attributes, removed {3} unrepresentable attributes and {4} type forwarders",
            floats, duplicates, paramTypes, unknownOwners, forwarders);
        return output.ToString();
    }

    // The generic parameter names of a header: the <...> list directly after the quoted type/method name
    // (for methods also directly followed by the parameter list), e.g. 'M'<(class 'I'<!!'T'>) 'T', 'U'>(
    static string[] GenericParameterNames(string header, bool isMethod)
    {
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i] == '\'')
            {
                i = header.IndexOf('\'', i + 1);
                if (i < 0) break;
                continue;
            }
            if (header[i] != '<' || i == 0 || header[i - 1] != '\'') continue;

            int end = MatchingBracket(header, i);
            if (end < 0) break;
            if (isMethod)
            {
                int next = end + 1;
                while (next < header.Length && char.IsWhiteSpace(header[next])) next++;
                //Generic arguments of the return type are followed by the method name, not the parameter list
                if (next >= header.Length || header[next] != '(') continue;
            }
            return SplitTopLevel(header.Substring(i + 1, end - i - 1))
                .Select(element => QuotedName.Matches(element).Cast<Match>().Select(m => m.Groups["name"].Value).LastOrDefault())
                .ToArray();
        }
        return new string[0];
    }

    // Lambda discards "(_, _) =>" are emitted as duplicate parameter names, which ilasm reports
    // (and the weaver treats any ilasm output as an error) -> make them unique. Discards are never read.
    static string RenameDuplicateParameters(string header, ref int duplicates)
    {
        int open = ParameterListStart(header);
        if (open < 0) return header;
        int close = MatchingParenthesis(header, open);
        if (close < 0) return header;

        List<string> parameters = SplitTopLevel(header.Substring(open + 1, close - open - 1));
        var seen = new HashSet<string>();
        bool changed = false;
        for (int i = 0; i < parameters.Count; i++)
        {
            Match name = TrailingName.Match(parameters[i]);
            if (!name.Success || seen.Add(name.Groups["name"].Value)) continue;
            parameters[i] = parameters[i].Substring(0, name.Index) + "'" + name.Groups["name"].Value + "__dup" + (++duplicates) + "'" + name.Groups["tail"].Value;
            changed = true;
        }
        return changed ? header.Substring(0, open + 1) + string.Join(",", parameters.ToArray()) + header.Substring(close) : header;
    }

    // The '(' of the parameter list: outside quotes and generic lists, directly after the (quoted) method name,
    // its generic parameter list or .ctor/.cctor
    static int ParameterListStart(string header)
    {
        int depth = 0;
        for (int i = 0; i < header.Length; i++)
        {
            char c = header[i];
            if (c == '\'') { i = header.IndexOf('\'', i + 1); if (i < 0) return -1; continue; }
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == '(' && depth == 0)
            {
                string before = header.Substring(0, i).TrimEnd();
                if (before.EndsWith("'") || before.EndsWith(">") || before.EndsWith("ctor")) return i;
            }
        }
        return -1;
    }

    static int MatchingParenthesis(string text, int start)
    {
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\'') { i = text.IndexOf('\'', i + 1); if (i < 0) return -1; continue; }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return -1;
    }

    static int MatchingBracket(string text, int start)
    {
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\'') { i = text.IndexOf('\'', i + 1); if (i < 0) return -1; continue; }
            if (c == '<') depth++;
            else if (c == '>' && --depth == 0) return i;
        }
        return -1;
    }

    static List<string> SplitTopLevel(string list)
    {
        var parts = new List<string>();
        int depth = 0, last = 0;
        for (int i = 0; i < list.Length; i++)
        {
            char c = list[i];
            if (c == '\'') { int close = list.IndexOf('\'', i + 1); if (close < 0) break; i = close; continue; }
            if (c == '<' || c == '(') depth++;
            else if (c == '>' || c == ')') depth--;
            else if (c == ',' && depth == 0) { parts.Add(list.Substring(last, i - last)); last = i + 1; }
        }
        parts.Add(list.Substring(last));
        return parts;
    }

    static string FloatBytes(bool isDouble, string value)
    {
        bool negative = value.StartsWith("-");
        bool nan = value.Contains("nan");
        byte[] bytes;
        if (isDouble)
        {
            double number = nan ? BitConverter.Int64BitsToDouble(negative ? unchecked((long)0xFFF8000000000000UL) : 0x7FF8000000000000L)
                                : (negative ? double.NegativeInfinity : double.PositiveInfinity);
            bytes = BitConverter.GetBytes(number);
        }
        else
        {
            bytes = nan ? BitConverter.GetBytes(negative ? unchecked((int)0xFFC00000U) : 0x7FC00000)
                        : BitConverter.GetBytes(negative ? float.NegativeInfinity : float.PositiveInfinity);
        }
        return "(" + string.Join(" ", bytes.Select(b => b.ToString("X2")).ToArray()) + ")";
    }
}
'@
}

# DllExportWeaver.Run() fails in PrepareIlAsm when called this way, so run its stages directly:
# ildasm -> patch IL -> ilasm (which also rewrites the export declarations).
# ilasm writes to a temporary folder first, so a failure does not destroy the input assembly.
[string]$tempDir = Join-Path ([IO.Path]::GetTempPath()) ('dllexport_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tempDir | Out-Null
$targetFile = $inputs.OutputFileName
[string]$outputDir = Join-Path $tempDir 'out'
New-Item -ItemType Directory $outputDir | Out-Null
$inputs.OutputFileName = Join-Path $outputDir ([IO.Path]::GetFileName($targetFile))
try {
    $weaver.GetType().GetMethod('RunIlDasm', $bf).Invoke($weaver, @($tempDir)) | Out-Null

    foreach ($il in Get-ChildItem $tempDir -Filter *.il) {
        $summary = $null
        $text = [DllExportIlPatcher]::Patch([IO.File]::ReadAllText($il.FullName), [ref]$summary)
        [IO.File]::WriteAllText($il.FullName, $text, (New-Object Text.UnicodeEncoding $false, $true))
        Write-Host "DllExport: patched $summary"
    }

    $ilAsm = [Activator]::CreateInstance($asm.GetType('NppPlugin.DllExport.Parsing.IlAsm'), @([IServiceProvider]$weaver, $inputs))
    $ilAsm.Timeout = $weaver.Timeout
    $ilAsm.TempDirectory = $tempDir
    $ilAsm.GetType().GetProperty('Exports', $bf).SetValue($ilAsm, $exports, $null)
    $weaver.GetType().GetMethod('RunIlAsm', $bf).Invoke($weaver, @($ilAsm)) | Out-Null

    Move-Item -Force $inputs.OutputFileName $targetFile
    $inputs.OutputFileName = $targetFile
}
catch {
    $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
    throw "DllExport failed: $($e.Message)"
}
finally {
    if ($KeepIntermediateFiles) { Write-Host "DllExport: intermediate files kept in $tempDir" }
    else { Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue }
}
Write-Host "DllExport: added $($exports.Count) unmanaged exports to $($inputs.OutputFileName) ($Platform)"
