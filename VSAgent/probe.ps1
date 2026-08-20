$exe = "obj\McpHost\VSAgent.McpHost.exe"
$pipe = "test-" + [Guid]::NewGuid().ToString("N")
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Resolve-Path $exe).Path
$psi.Arguments = "--pipe $pipe"
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$p.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"probe","version":"0.0.1"}}}')
$p.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
$p.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list"}')
Start-Sleep -Milliseconds 1500
$p.StandardInput.Close()
$out = $p.StandardOutput.ReadToEnd()
$p.WaitForExit(2000) | Out-Null
$names = [regex]::Matches($out, '"name":"(vs_[a-z_]+)"') | ForEach-Object { $_.Groups[1].Value }
Write-Host "Total tools: $($names.Count)"
$names | Sort-Object | ForEach-Object { Write-Host ("  - " + $_) }
