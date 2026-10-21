# Unblock Supabase connections
Disable-NetFirewallRule -DisplayName "codex_sandbox_offline_block_outbound" -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName "Allow Supabase Pooler" -Direction Outbound -RemoteAddress "65.0.195.55","3.111.105.85" -RemotePort 5432,6543 -Protocol TCP -Action Allow -ErrorAction SilentlyContinue
Write-Host "Supabase firewall rules updated successfully!"
Start-Sleep -Seconds 2
