# =============================================================
#  ðŸ¦Š inFaux Uninstaller Script
#  https://github.com/TalviFox/inFaux
#  Cleanly removes inFaux background tasks, shortcuts, registry entries, and program files.
# =============================================================

[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$Silent
)

# Console environment normalization (ensures 24-bit TrueColor)
try {
    if (-not ([System.Management.Automation.PSTypeName]'Win32.ConsoleVT').Type) {
        Add-Type -MemberDefinition @'
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GetStdHandle(int nStdHandle);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetConsoleTextAttribute(IntPtr hConsoleHandle, ushort wAttributes);
'@ -Name 'ConsoleVT' -Namespace 'Win32'
    }

    $hStdOut = [Win32.ConsoleVT]::GetStdHandle(-11)
    [Win32.ConsoleVT]::SetConsoleTextAttribute($hStdOut, 0x0007) | Out-Null
    $consoleMode = 0
    if ([Win32.ConsoleVT]::GetConsoleMode($hStdOut, [ref]$consoleMode)) {
        [Win32.ConsoleVT]::SetConsoleMode($hStdOut, ($consoleMode -bor 0x0004)) | Out-Null
    }
} catch {}

try {
    $host.UI.RawUI.BackgroundColor = 'Black'
    $host.UI.RawUI.ForegroundColor = 'White'
    Clear-Host
} catch {}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = "SilentlyContinue"

$OutputEncoding = [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$esc = [char]27
$u = [char]0x2580
$d = [char]0x2584

$banner = @"
$esc[38;2;15;33;21m$esc[49m$d$esc[38;2;19;35;21m$esc[48;2;47;59;32m$u$esc[0m $esc[38;2;103;109;112m$esc[49m$d$esc[38;2;120;126;129m$esc[49m$d$esc[38;2;109;113;119m$esc[49m$d$esc[38;2;110;116;119m$esc[49m$d$esc[38;2;110;116;123m$esc[49m$d$esc[38;2;116;122;126m$esc[49m$d$esc[38;2;119;124;127m$esc[49m$d$esc[38;2;120;124;127m$esc[49m$d$esc[38;2;120;124;127m$esc[49m$d$esc[38;2;120;123;129m$esc[49m$d$esc[38;2;120;126;129m$esc[49m$d$esc[38;2;120;124;129m$esc[49m$d$esc[38;2;120;126;129m$esc[49m$d$esc[38;2;120;126;129m$esc[49m$d$esc[38;2;120;124;129m$esc[49m$d$esc[38;2;124;129;131m$esc[49m$d$esc[38;2;130;133;134m$esc[49m$d$esc[38;2;120;126;129m$esc[49m$d$esc[38;2;123;127;130m$esc[49m$d$esc[38;2;123;129;133m$esc[49m$d$esc[38;2;134;138;143m$esc[49m$d$esc[38;2;110;115;116m$esc[49m$d$esc[0m $esc[38;2;0;26;24m$esc[49m$d$esc[0m $esc[0m   
$esc[0m $esc[0m $esc[38;2;101;103;108m$esc[48;2;119;122;126m$u$esc[38;2;157;161;169m$esc[48;2;143;148;154m$u$esc[38;2;137;141;148m$esc[48;2;179;183;189m$u$esc[38;2;151;158;164m$esc[48;2;171;176;183m$u$esc[38;2;148;154;159m$esc[48;2;176;182;186m$u$esc[38;2;148;155;162m$esc[48;2;179;185;189m$u$esc[38;2;150;157;164m$esc[48;2;180;186;190m$u$esc[38;2;150;157;161m$esc[48;2;183;189;193m$u$esc[38;2;151;158;162m$esc[48;2;185;190;194m$u$esc[38;2;151;157;164m$esc[48;2;189;196;199m$u$esc[38;2;151;158;165m$esc[48;2;192;199;203m$u$esc[38;2;151;158;162m$esc[48;2;194;200;204m$u$esc[38;2;150;157;161m$esc[48;2;197;200;206m$u$esc[38;2;151;158;164m$esc[48;2;200;206;210m$u$esc[38;2;152;158;164m$esc[48;2;206;211;215m$u$esc[38;2;151;158;162m$esc[48;2;213;218;222m$u$esc[38;2;151;158;164m$esc[48;2;214;218;222m$u$esc[38;2;151;157;166m$esc[48;2;218;222;228m$u$esc[38;2;154;159;166m$esc[48;2;224;229;234m$u$esc[38;2;155;161;166m$esc[48;2;222;227;232m$u$esc[38;2;155;161;168m$esc[48;2;222;228;232m$u$esc[38;2;145;151;155m$esc[48;2;218;222;227m$u$esc[38;2;173;178;183m$esc[48;2;158;162;166m$u$esc[38;2;110;113;117m$esc[48;2;124;127;134m$u$esc[0m $esc[0m $esc[0m   $([char]::ConvertFromUtf32(0x1F98A)) $esc[1;33minFaux Uninstaller$esc[0m
$esc[0m $esc[0m $esc[38;2;112;113;117m$esc[48;2;112;116;119m$u$esc[38;2;175;182;186m$esc[48;2;171;178;183m$u$esc[38;2;164;171;175m$esc[48;2;164;169;173m$u$esc[38;2;161;166;171m$esc[48;2;164;169;173m$u$esc[38;2;161;166;171m$esc[48;2;165;171;175m$u$esc[38;2;164;171;175m$esc[48;2;169;175;179m$u$esc[38;2;168;173;178m$esc[48;2;161;166;172m$u$esc[38;2;172;178;182m$esc[48;2;110;116;120m$u$esc[38;2;180;186;190m$esc[48;2;157;162;166m$u$esc[38;2;179;185;190m$esc[48;2;190;196;200m$u$esc[38;2;186;192;196m$esc[48;2;185;190;196m$u$esc[38;2;189;194;199m$esc[48;2;192;199;203m$u$esc[38;2;193;197;203m$esc[48;2;194;199;204m$u$esc[38;2;196;201;206m$esc[48;2;194;201;204m$u$esc[38;2;200;206;208m$esc[48;2;204;211;214m$u$esc[38;2;211;217;221m$esc[48;2;196;201;206m$u$esc[38;2;213;218;222m$esc[48;2;130;136;138m$u$esc[38;2;220;225;229m$esc[48;2;204;208;214m$u$esc[38;2;221;227;232m$esc[48;2;225;232;236m$u$esc[38;2;222;227;232m$esc[48;2;222;228;232m$u$esc[38;2;221;225;231m$esc[48;2;222;227;232m$u$esc[38;2;228;234;238m$esc[48;2;228;234;238m$u$esc[38;2;210;214;221m$esc[48;2;208;214;220m$u$esc[38;2;113;113;120m$esc[48;2;112;115;120m$u$esc[0m $esc[0m $esc[0m   $esc[90mClean & Complete System Removal$esc[0m
$esc[0m $esc[0m $esc[38;2;113;115;117m$esc[48;2;112;115;117m$u$esc[38;2;172;176;182m$esc[48;2;169;176;180m$u$esc[38;2;161;166;173m$esc[48;2;161;166;171m$u$esc[38;2;164;169;173m$esc[48;2;162;169;173m$u$esc[38;2;165;172;176m$esc[48;2;165;171;175m$u$esc[38;2;169;176;180m$esc[48;2;171;178;182m$u$esc[38;2;147;152;157m$esc[48;2;140;145;150m$u$esc[38;2;145;151;155m$esc[48;2;172;179;183m$u$esc[38;2;103;108;112m$esc[48;2;143;150;154m$u$esc[38;2;155;159;164m$esc[48;2;92;98;102m$u$esc[38;2;189;194;199m$esc[48;2;151;157;161m$u$esc[38;2;185;189;193m$esc[48;2;187;193;197m$u$esc[38;2;187;192;197m$esc[48;2;189;194;199m$u$esc[38;2;196;203;207m$esc[48;2;178;185;187m$u$esc[38;2;190;194;200m$esc[48;2;102;106;110m$u$esc[38;2;124;129;133m$esc[48;2;150;154;158m$u$esc[38;2;166;172;176m$esc[48;2;207;213;217m$u$esc[38;2;183;190;194m$esc[48;2;169;175;179m$u$esc[38;2;224;229;234m$esc[48;2;222;228;232m$u$esc[38;2;225;229;235m$esc[48;2;222;228;234m$u$esc[38;2;225;231;236m$esc[48;2;227;232;236m$u$esc[38;2;225;231;235m$esc[48;2;231;236;241m$u$esc[38;2;206;211;217m$esc[48;2;207;213;218m$u$esc[38;2;110;112;117m$esc[48;2;109;112;116m$u$esc[0m $esc[0m $esc[0m   
$esc[0m $esc[38;2;70;82;85m$esc[48;2;186;190;194m$u$esc[38;2;148;152;158m$esc[48;2;144;150;154m$u$esc[38;2;157;162;168m$esc[48;2;158;164;169m$u$esc[38;2;162;169;173m$esc[48;2;164;169;173m$u$esc[38;2;159;166;169m$esc[48;2;161;168;172m$u$esc[38;2;158;164;168m$esc[48;2;161;166;171m$u$esc[38;2;162;168;175m$esc[48;2;164;172;175m$u$esc[38;2;141;145;150m$esc[48;2;154;159;164m$u$esc[38;2;158;162;168m$esc[48;2;155;159;165m$u$esc[38;2;166;172;176m$esc[48;2;168;173;178m$u$esc[38;2;123;127;133m$esc[48;2;178;185;187m$u$esc[38;2;150;152;157m$esc[48;2;189;196;200m$u$esc[38;2;183;189;193m$esc[48;2;185;190;194m$u$esc[38;2;189;193;199m$esc[48;2;183;190;194m$u$esc[38;2;171;176;179m$esc[48;2;192;197;201m$u$esc[38;2;131;136;140m$esc[48;2;197;203;207m$u$esc[38;2;185;190;192m$esc[48;2;194;201;206m$u$esc[38;2;192;197;201m$esc[48;2;178;185;186m$u$esc[38;2;173;178;182m$esc[48;2;187;193;196m$u$esc[38;2;221;227;231m$esc[48;2;214;217;222m$u$esc[38;2;221;227;231m$esc[48;2;220;227;231m$u$esc[38;2;218;224;228m$esc[48;2;221;227;231m$u$esc[38;2;224;228;234m$esc[48;2;224;229;234m$u$esc[38;2;196;201;204m$esc[48;2;194;201;204m$u$esc[38;2;148;151;157m$esc[48;2;145;152;151m$u$esc[38;2;71;81;84m$esc[48;2;186;190;193m$u$esc[0m $esc[0m   $esc[1;37mComponents To Be Removed:$esc[0m
$esc[38;2;15;26;25m$esc[48;2;14;26;24m$u$esc[38;2;173;179;183m$esc[48;2;175;180;185m$u$esc[38;2;140;144;150m$esc[48;2;140;145;150m$u$esc[38;2;161;164;169m$esc[48;2;158;164;169m$u$esc[38;2;161;168;172m$esc[48;2;159;165;168m$u$esc[38;2;158;164;169m$esc[48;2;165;171;175m$u$esc[38;2;165;171;175m$esc[48;2;136;141;145m$u$esc[38;2;131;138;140m$esc[48;2;87;94;94m$u$esc[38;2;161;166;172m$esc[48;2;148;157;158m$u$esc[38;2;175;180;185m$esc[48;2;99;108;108m$u$esc[38;2;182;189;193m$esc[48;2;112;119;120m$u$esc[38;2;133;140;141m$esc[48;2;98;105;105m$u$esc[38;2;101;106;108m$esc[48;2;112;117;119m$u$esc[38;2;137;143;144m$esc[48;2;166;171;175m$u$esc[38;2;190;194;199m$esc[48;2;137;141;143m$u$esc[38;2;192;196;200m$esc[48;2;103;110;112m$u$esc[38;2;199;200;204m$esc[48;2;133;138;140m$u$esc[38;2;196;200;206m$esc[48;2;166;171;175m$u$esc[38;2;200;206;210m$esc[48;2;134;141;143m$u$esc[38;2;201;206;211m$esc[48;2;141;148;150m$u$esc[38;2;204;210;214m$esc[48;2;159;164;168m$u$esc[38;2;206;211;215m$esc[48;2;164;171;171m$u$esc[38;2;208;214;218m$esc[48;2;211;217;222m$u$esc[38;2;221;227;232m$esc[48;2;214;221;225m$u$esc[38;2;199;203;208m$esc[48;2;196;200;204m$u$esc[38;2;141;145;148m$esc[48;2;141;148;150m$u$esc[38;2;175;180;183m$esc[48;2;176;182;185m$u$esc[0m $esc[0m     $esc[31m*$esc[0m Application files (%LOCALAPPDATA%\Programs\inFaux)
$esc[0m $esc[38;2;175;180;185m$esc[48;2;173;179;183m$u$esc[38;2;138;144;150m$esc[48;2;138;145;150m$u$esc[38;2;157;164;168m$esc[48;2;155;161;165m$u$esc[38;2;159;166;169m$esc[48;2;154;159;165m$u$esc[38;2;158;165;168m$esc[48;2;158;164;171m$u$esc[38;2;158;164;169m$esc[48;2;143;150;152m$u$esc[38;2;101;106;109m$esc[48;2;75;82;82m$u$esc[38;2;133;140;141m$esc[48;2;102;109;110m$u$esc[38;2;99;105;106m$esc[48;2;124;130;133m$u$esc[38;2;110;116;119m$esc[48;2;124;131;133m$u$esc[38;2;81;89;89m$esc[48;2;78;85;85m$u$esc[38;2;94;102;102m$esc[48;2;164;169;172m$u$esc[38;2;148;154;157m$esc[48;2;165;172;176m$u$esc[38;2;108;112;113m$esc[48;2;84;91;91m$u$esc[38;2;81;87;88m$esc[48;2;94;99;101m$u$esc[38;2;87;91;92m$esc[48;2;102;108;109m$u$esc[38;2;154;159;161m$esc[48;2;102;106;108m$u$esc[38;2;117;124;126m$esc[48;2;99;103;105m$u$esc[38;2;116;122;123m$esc[48;2;124;129;130m$u$esc[38;2;67;73;74m$esc[48;2;109;113;115m$u$esc[38;2;193;194;199m$esc[48;2;164;168;169m$u$esc[38;2;201;207;211m$esc[48;2;199;204;208m$u$esc[38;2;203;204;211m$esc[48;2;200;201;207m$u$esc[38;2;187;192;194m$esc[48;2;186;190;194m$u$esc[38;2;141;148;151m$esc[48;2;140;147;150m$u$esc[38;2;175;180;185m$esc[48;2;175;180;185m$u$esc[0m $esc[0m     $esc[31m*$esc[0m Scheduled task autostart (Standard user)
$esc[0m $esc[38;2;173;179;182m$esc[48;2;173;179;183m$u$esc[38;2;138;145;150m$esc[48;2;138;144;148m$u$esc[38;2;148;154;159m$esc[48;2;150;154;159m$u$esc[38;2;150;155;159m$esc[48;2;150;155;159m$u$esc[38;2;154;159;165m$esc[48;2;152;158;162m$u$esc[38;2;145;151;158m$esc[48;2;157;162;168m$u$esc[38;2;131;137;144m$esc[48;2;155;158;165m$u$esc[38;2;137;144;145m$esc[48;2;120;127;127m$u$esc[38;2;152;159;162m$esc[48;2;130;136;137m$u$esc[38;2;158;164;168m$esc[48;2;127;133;136m$u$esc[38;2;151;158;159m$esc[48;2;161;166;171m$u$esc[38;2;157;164;166m$esc[48;2;134;141;143m$u$esc[38;2;171;178;180m$esc[48;2;131;138;141m$u$esc[38;2;152;159;162m$esc[48;2;145;152;155m$u$esc[38;2;141;147;150m$esc[48;2;144;151;154m$u$esc[38;2;168;173;176m$esc[48;2;133;138;141m$u$esc[38;2;151;157;159m$esc[48;2;154;159;161m$u$esc[38;2;162;168;169m$esc[48;2;138;144;145m$u$esc[38;2;173;179;182m$esc[48;2;148;155;158m$u$esc[38;2;187;193;197m$esc[48;2;185;190;194m$u$esc[38;2;182;187;190m$esc[48;2;194;200;206m$u$esc[38;2;200;206;210m$esc[48;2;193;199;203m$u$esc[38;2;201;208;213m$esc[48;2;197;200;204m$u$esc[38;2;187;193;196m$esc[48;2;185;187;190m$u$esc[38;2;141;147;150m$esc[48;2;141;145;148m$u$esc[38;2;175;179;183m$esc[48;2;173;179;183m$u$esc[0m $esc[0m     $esc[31m*$esc[0m Start Menu & Desktop shortcuts
$esc[0m $esc[38;2;171;176;182m$esc[48;2;182;186;190m$u$esc[38;2;138;145;148m$esc[48;2;138;145;148m$u$esc[38;2;150;155;159m$esc[48;2;143;147;152m$u$esc[38;2;143;150;154m$esc[48;2;136;141;145m$u$esc[38;2;150;154;159m$esc[48;2;145;151;155m$u$esc[38;2;150;157;161m$esc[48;2;148;154;159m$u$esc[38;2;151;158;162m$esc[48;2;151;157;162m$u$esc[38;2;148;155;158m$esc[48;2;154;158;164m$u$esc[38;2;154;159;164m$esc[48;2;157;162;168m$u$esc[38;2;154;161;164m$esc[48;2;159;165;169m$u$esc[38;2;157;162;166m$esc[48;2;159;166;171m$u$esc[38;2;162;169;173m$esc[48;2;161;166;172m$u$esc[38;2;159;164;166m$esc[48;2;168;171;175m$u$esc[38;2;158;165;166m$esc[48;2;166;172;178m$u$esc[38;2;166;172;176m$esc[48;2;168;175;179m$u$esc[38;2;166;173;176m$esc[48;2;171;178;180m$u$esc[38;2;173;179;183m$esc[48;2;172;180;182m$u$esc[38;2;176;182;185m$esc[48;2;180;182;187m$u$esc[38;2;178;183;187m$esc[48;2;183;185;190m$u$esc[38;2;187;193;197m$esc[48;2;182;187;192m$u$esc[38;2;190;196;200m$esc[48;2;180;186;190m$u$esc[38;2;187;193;197m$esc[48;2;178;183;189m$u$esc[38;2;193;199;203m$esc[48;2;183;190;193m$u$esc[38;2;183;186;190m$esc[48;2;176;179;182m$u$esc[38;2;140;145;147m$esc[48;2;143;145;148m$u$esc[38;2;172;178;182m$esc[48;2;183;189;190m$u$esc[38;2;12;26;21m$esc[49m$u$esc[0m     $esc[31m*$esc[0m Windows Installed Apps registration
$esc[0m $esc[38;2;96;106;110m$esc[49m$u$esc[38;2;152;159;162m$esc[48;2;117;120;126m$u$esc[38;2;140;145;148m$esc[48;2;150;155;161m$u$esc[38;2;136;141;145m$esc[48;2;133;138;143m$u$esc[38;2;141;148;151m$esc[48;2;138;143;148m$u$esc[38;2;144;150;155m$esc[48;2;144;150;154m$u$esc[38;2;143;148;152m$esc[48;2;144;150;154m$u$esc[38;2;147;152;157m$esc[48;2;148;154;158m$u$esc[38;2;151;157;164m$esc[48;2;152;157;162m$u$esc[38;2;154;161;165m$esc[48;2;152;158;162m$u$esc[38;2;157;162;166m$esc[48;2;155;161;165m$u$esc[38;2;162;168;172m$esc[48;2;157;162;165m$u$esc[38;2;162;169;173m$esc[48;2;161;166;171m$u$esc[38;2;165;171;175m$esc[48;2;162;166;172m$u$esc[38;2;166;172;176m$esc[48;2;164;168;172m$u$esc[38;2;169;175;179m$esc[48;2;165;171;175m$u$esc[38;2;169;176;180m$esc[48;2;166;173;178m$u$esc[38;2;173;178;183m$esc[48;2;173;178;182m$u$esc[38;2;179;182;186m$esc[48;2;178;180;185m$u$esc[38;2;178;183;187m$esc[48;2;176;182;186m$u$esc[38;2;176;182;186m$esc[48;2;178;183;187m$u$esc[38;2;178;185;187m$esc[48;2;182;185;190m$u$esc[38;2;183;189;192m$esc[48;2;187;190;192m$u$esc[38;2;171;176;176m$esc[48;2;183;187;189m$u$esc[38;2;154;158;157m$esc[48;2;116;116;120m$u$esc[38;2;99;105;106m$esc[49m$u$esc[0m $esc[0m   
$esc[38;2;18;36;22m$esc[49m$u$esc[38;2;46;47;24m$esc[48;2;17;28;17m$u$esc[38;2;101;105;110m$esc[48;2;106;110;116m$u$esc[38;2;152;158;162m$esc[48;2;144;150;154m$u$esc[38;2;133;138;143m$esc[48;2;127;134;138m$u$esc[38;2;134;141;145m$esc[48;2;136;141;145m$u$esc[38;2;141;147;151m$esc[48;2;137;143;147m$u$esc[38;2;143;148;154m$esc[48;2;143;150;152m$u$esc[38;2;150;155;159m$esc[48;2;147;152;157m$u$esc[38;2;152;158;162m$esc[48;2;147;152;157m$u$esc[38;2;151;158;161m$esc[48;2;150;155;159m$u$esc[38;2;155;159;166m$esc[48;2;152;158;164m$u$esc[38;2;157;162;166m$esc[48;2;157;161;168m$u$esc[38;2;157;162;166m$esc[48;2;161;166;171m$u$esc[38;2;164;169;172m$esc[48;2;165;169;173m$u$esc[38;2;164;169;173m$esc[48;2;165;171;175m$u$esc[38;2;165;171;175m$esc[48;2;168;173;179m$u$esc[38;2;169;175;179m$esc[48;2;171;176;182m$u$esc[38;2;169;175;179m$esc[48;2;169;175;179m$u$esc[38;2;173;179;183m$esc[48;2;169;175;180m$u$esc[38;2;175;180;186m$esc[48;2;172;178;183m$u$esc[38;2;176;183;187m$esc[48;2;173;179;185m$u$esc[38;2;178;183;189m$esc[48;2;173;179;185m$u$esc[38;2;185;189;193m$esc[48;2;175;180;186m$u$esc[38;2;182;187;189m$esc[48;2;176;180;186m$u$esc[38;2;103;108;109m$esc[48;2;108;110;113m$u$esc[0m $esc[0m $esc[0m   $esc[90mZero residual drivers or background services.$esc[0m
$esc[0m $esc[0m $esc[38;2;108;110;113m$esc[48;2;105;109;112m$u$esc[38;2;143;150;154m$esc[48;2;127;131;137m$u$esc[38;2;113;119;123m$esc[48;2;136;140;144m$u$esc[38;2;116;122;126m$esc[48;2;120;126;131m$u$esc[38;2;117;123;127m$esc[48;2;120;126;130m$u$esc[38;2;123;129;133m$esc[48;2;127;133;137m$u$esc[38;2;129;134;137m$esc[48;2;131;137;141m$u$esc[38;2;134;138;144m$esc[48;2;127;133;137m$u$esc[38;2;140;147;150m$esc[48;2;136;141;145m$u$esc[38;2;133;140;141m$esc[48;2;127;133;136m$u$esc[38;2;141;145;152m$esc[48;2;140;145;150m$u$esc[38;2;138;144;148m$esc[48;2;137;141;144m$u$esc[38;2;144;148;151m$esc[48;2;141;145;147m$u$esc[38;2;145;151;155m$esc[48;2;141;147;148m$u$esc[38;2;147;152;155m$esc[48;2;143;148;151m$u$esc[38;2;152;158;162m$esc[48;2;150;154;157m$u$esc[38;2;164;169;173m$esc[48;2;171;176;180m$u$esc[38;2;166;172;178m$esc[48;2;173;179;185m$u$esc[38;2;169;175;180m$esc[48;2;175;180;185m$u$esc[38;2;171;176;180m$esc[48;2;175;180;186m$u$esc[38;2;171;176;180m$esc[48;2;176;182;186m$u$esc[38;2;173;179;185m$esc[48;2;185;190;194m$u$esc[38;2;175;179;185m$esc[48;2;137;141;145m$u$esc[38;2;105;105;108m$esc[48;2;106;110;115m$u$esc[0m $esc[0m $esc[0m   
$esc[0m $esc[0m $esc[38;2;119;122;124m$esc[49m$u$esc[38;2;117;123;127m$esc[48;2;126;129;136m$u$esc[38;2;113;117;123m$esc[48;2;124;129;133m$u$esc[38;2;131;137;140m$esc[48;2;116;119;122m$u$esc[38;2;130;136;140m$esc[48;2;117;120;124m$u$esc[38;2;130;137;141m$esc[48;2;116;119;124m$u$esc[38;2;133;140;143m$esc[48;2;119;122;126m$u$esc[38;2;136;141;145m$esc[48;2;108;112;116m$u$esc[38;2;144;151;152m$esc[48;2;35;42;42m$u$esc[38;2;148;152;157m$esc[48;2;26;33;32m$u$esc[38;2;150;154;159m$esc[48;2;28;35;33m$u$esc[38;2;152;158;161m$esc[48;2;25;33;32m$u$esc[38;2;152;158;161m$esc[48;2;25;35;32m$u$esc[38;2;154;159;164m$esc[48;2;26;33;33m$u$esc[38;2;155;161;165m$esc[48;2;26;33;33m$u$esc[38;2;155;159;164m$esc[48;2;32;39;40m$u$esc[38;2;147;152;157m$esc[48;2;99;101;105m$u$esc[38;2;145;152;157m$esc[48;2;109;110;116m$u$esc[38;2;147;152;157m$esc[48;2;108;110;115m$u$esc[38;2;145;152;155m$esc[48;2;109;113;116m$u$esc[38;2;150;155;159m$esc[48;2;105;109;110m$u$esc[38;2;131;134;138m$esc[48;2;120;124;130m$u$esc[38;2;136;140;145m$esc[48;2;123;124;130m$u$esc[38;2;124;126;130m$esc[49m$u$esc[0m $esc[0m $esc[0m   $esc[90mVersion 1.1.1 $([char]0x2022) FoxDen Software$esc[0m
$esc[38;2;18;33;21m$esc[48;2;28;35;17m$u$esc[38;2;11;38;29m$esc[48;2;15;32;17m$u$esc[0m $esc[0m $esc[38;2;24;38;38m$esc[49m$u$esc[38;2;19;35;33m$esc[49m$u$esc[38;2;21;36;35m$esc[49m$u$esc[38;2;21;35;35m$esc[49m$u$esc[38;2;22;38;38m$esc[49m$u$esc[38;2;21;33;35m$esc[49m$u$esc[0m $esc[0m $esc[38;2;8;26;24m$esc[49m$d$esc[38;2;17;31;29m$esc[49m$d$esc[0m $esc[38;2;12;28;26m$esc[49m$d$esc[0m $esc[0m $esc[0m $esc[38;2;15;25;26m$esc[49m$u$esc[0m $esc[0m $esc[0m $esc[38;2;29;32;28m$esc[48;2;19;35;17m$u$esc[0m $esc[0m $esc[38;2;7;29;26m$esc[49m$u$esc[38;2;4;26;18m$esc[49m$u$esc[0m   
"@

$banner = $banner.Replace("$esc[49m", "$esc[48;2;0;0;0m")
Write-Host $banner

# 1. Confirmation Prompt
if (-not ($Force -or $Silent)) {
    Write-Host ""
    Write-Host "[?] Are you sure you want to completely uninstall inFaux?" -ForegroundColor Yellow
    $confirm = Read-Host "    Type 'YES' to proceed with uninstall, or press Enter to cancel"
    if ($confirm -ne "YES") {
        Write-Host "`n[*] Uninstall cancelled. inFaux remains untouched." -ForegroundColor Cyan
        Start-Sleep -Seconds 2
        return
    }
    Write-Host ""
}

# 2. Stop running inFaux processes
Write-Host "[*] Terminating running inFaux processes..." -ForegroundColor Cyan
Get-Process -Name "inFaux", "inFox" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

# 3. Remove Task Scheduler background tasks
Write-Host "[*] Removing Task Scheduler background tasks..." -ForegroundColor Cyan
Unregister-ScheduledTask -TaskName "inFaux" -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
Unregister-ScheduledTask -TaskName "inFox" -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
schtasks.exe /Delete /TN "inFaux" /F *>$null 2>$null
schtasks.exe /Delete /TN "inFox" /F *>$null 2>$null

# 4. Remove Start Menu shortcuts (User & Legacy Machine)
Write-Host "[*] Removing Start Menu shortcuts..." -ForegroundColor Cyan
$userShortcut = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\inFaux.lnk"
$legacyShortcut = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\inFaux.lnk"
if (Test-Path $userShortcut) { Remove-Item -Path $userShortcut -Force -ErrorAction SilentlyContinue }
if (Test-Path $legacyShortcut) { Remove-Item -Path $legacyShortcut -Force -ErrorAction SilentlyContinue }

# 5. Remove Windows Installed Apps entry (User HKCU & Legacy HKLM)
Write-Host "[*] Removing Windows Installed Application entry..." -ForegroundColor Cyan
Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\inFaux" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\inFaux" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\inFox" -Recurse -Force -ErrorAction SilentlyContinue

# 6. Remove User AppData Programs directory
$installDir = "$env:LOCALAPPDATA\Programs\inFaux"
Write-Host "[*] Removing files from $installDir..." -ForegroundColor Cyan

# Change PowerShell current directory to TEMP to release directory handle
Set-Location $env:TEMP

# Remove all non-executing files immediately
if (Test-Path $installDir) {
    Get-ChildItem -Path $installDir -Exclude "uninstall.ps1" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

# Self-contained batch cleaner with retry loop to delete the uninstaller itself once PowerShell exits
$tempBatch = Join-Path $env:TEMP "inFaux_uninstall_cleanup.cmd"
@"
@echo off
:retry
timeout /t 1 /nobreak >nul
rd /s /q "$installDir" 2>nul
if exist "$installDir" goto retry
del /f /q "%~f0" 2>nul
"@ | Set-Content -Path $tempBatch -Encoding ASCII

Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$tempBatch`"" -WindowStyle Hidden

Write-Host @"

  ======================================================
     âœ… inFaux has been uninstalled successfully.
  ======================================================
  Settings & telemetry logs are preserved in:
  $env:LOCALAPPDATA\inFaux (safe for future installs)
  ======================================================
"@ -ForegroundColor Green

if (-not ($Force -or $Silent)) {
    Read-Host "`n    Press [Enter] to exit"
}

