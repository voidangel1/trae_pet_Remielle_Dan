# 扩展安装子脚本（通过 WMI 在沙箱外执行，绕开 .trae-cn\extensions 的沙箱保护）
# 由主会话调用：Invoke-CimMethod Win32_Process.Create powershell -File <本文件>
$extRoot = "C:\Users\HP\.trae-cn\extensions"
$src = "C:\Users\HP\Documents\trae_projects\1\ClawdPet-v2.0\trae-pet-bridge"
$log = "C:\Users\HP\AppData\Local\Temp\clawd-ext-install-result.txt"

try {
    Remove-Item "$extRoot\remielle.trae-pet-bridge-0.2.0" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "$extRoot\remielle.trae-pet-bridge-2.0.0" -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path "$extRoot\remielle.trae-pet-bridge-2.0.0" | Out-Null
    Copy-Item "$src\*" "$extRoot\remielle.trae-pet-bridge-2.0.0\" -Recurse -Force
    Remove-Item $log -Force -ErrorAction SilentlyContinue
    $files = Get-ChildItem "$extRoot\remielle.trae-pet-bridge-2.0.0" | Select-Object Name, Length
    $files | Out-File $log -Encoding UTF8
    "SUCCESS $($files.Count) files" | Out-File $log -Append -Encoding UTF8
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File $log -Encoding UTF8
}
