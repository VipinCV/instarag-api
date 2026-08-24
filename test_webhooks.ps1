$baseUrl = "http://localhost:5000/api/webhook/instagram"

Write-Host "1. Valid verification:"
$res1 = Invoke-WebRequest -Uri "$baseUrl?hub.mode=subscribe&hub.verify_token=instarag_verify_2024&hub.challenge=challenge123" -UseBasicParsing
Write-Host "GET valid -> $($res1.StatusCode) $($res1.Content)"

Write-Host "`n2. Invalid verification:"
try {
    Invoke-WebRequest -Uri "$baseUrl?hub.mode=subscribe&hub.verify_token=wrong_token&hub.challenge=challenge123" -UseBasicParsing -ErrorAction Stop
} catch {
    Write-Host "GET invalid -> $($_.Exception.Response.StatusCode.value__) $($_.Exception.Response.StatusDescription)"
}

Write-Host "`n3. POST message payload:"
$payload = @{
    object = "instagram"
    entry = @(
        @{
            id = "entry_1"
            time = 1700000000
            messaging = @(
                @{
                    sender = @{ id = "user_123" }
                    recipient = @{ id = "page_456" }
                    timestamp = 1700000000
                    message = @{
                        mid = "mid_abc_123"
                        text = "Do you have sneakers?"
                    }
                }
            )
        }
    )
} | ConvertTo-Json -Depth 5

$res3 = Invoke-WebRequest -Uri $baseUrl -Method Post -Body $payload -ContentType "application/json" -UseBasicParsing
Write-Host "POST message -> $($res3.StatusCode) $($res3.Content)"
