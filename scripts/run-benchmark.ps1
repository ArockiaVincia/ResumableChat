# Resumable Conversation Verification Benchmark Runner
Write-Host "Running Problem 1 Verification Benchmark..." -ForegroundColor Cyan
dotnet test --filter "Category=Benchmark" --logger "console;verbosity=normal"
