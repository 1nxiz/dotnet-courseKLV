# =====================================================================
# Розгортання всіх трьох баз з нуля (Windows PowerShell / PowerShell 7).
# Порядок: p1 schema -> procedures -> seed; p2 schema -> seed; security; p3 collections -> seed.
# Повторний запуск безпечний (усі скрипти ідемпотентні).
#
# Варіант А (рекомендовано) — бази в Docker Desktop:
#   docker run -d --name library-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=Library_Pass123 -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
#   docker run -d --name library-mongo -p 27017:27017 mongo:7
#   .\db\deploy.ps1
#
# Варіант Б — локальний SQL Server і mongosh:
#   .\db\deploy.ps1 -UseDocker:$false -SqlServer "localhost" -SaPassword "..." -MongoUri "mongodb://localhost:27017"
# =====================================================================
param(
    [bool]   $UseDocker        = $true,
    [string] $SqlContainer     = "library-sql",
    [string] $MongoContainer   = "library-mongo",
    [string] $SqlServer        = "localhost,1433",
    [string] $SaPassword       = "Library_Pass123",
    [string] $MongoUri         = "mongodb://localhost:27017",
    [string] $LoansAppPassword = "LoansSvc#2026pw",
    [string] $BooksAppPassword = "BooksSvc#2026pw"
)
$ErrorActionPreference = "Stop"
$DbDir = $PSScriptRoot

if ($UseDocker) {
    docker exec $SqlContainer mkdir -p /tmp/library-db | Out-Null
    docker cp "$DbDir/." "${SqlContainer}:/tmp/library-db"
    docker exec $MongoContainer mkdir -p /tmp/library-db | Out-Null
    docker cp "$DbDir/." "${MongoContainer}:/tmp/library-db"
}

function Invoke-Sql([string] $File) {
    Write-Host ">> sqlcmd $File" -ForegroundColor Cyan
    if ($UseDocker) {
        docker exec -i $SqlContainer /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $SaPassword -b -I `
            -i "/tmp/library-db/$File" -v LoansAppPassword=$LoansAppPassword BooksAppPassword=$BooksAppPassword
    } else {
        sqlcmd -C -S $SqlServer -U sa -P $SaPassword -b -I -i (Join-Path $DbDir $File) `
            -v LoansAppPassword=$LoansAppPassword BooksAppPassword=$BooksAppPassword
    }
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed on $File" }
}

function Invoke-Mongo([string] $File) {
    Write-Host ">> mongosh $File" -ForegroundColor Cyan
    if ($UseDocker) {
        docker exec -i $MongoContainer mongosh --quiet "mongodb://localhost:27017" --file "/tmp/library-db/$File"
    } else {
        mongosh --quiet $MongoUri --file (Join-Path $DbDir $File)
    }
    if ($LASTEXITCODE -ne 0) { throw "mongosh failed on $File" }
}

Write-Host "== Проєкт №1: LoansDb (SQL Server)"
Invoke-Sql "p1/schema.sql"
Invoke-Sql "p1/procedures.sql"
Invoke-Sql "p1/seed.sql"

Write-Host "== Проєкт №2: BooksDb (SQL Server)"
Invoke-Sql "p2/schema.sql"
Invoke-Sql "p2/seed.sql"

Write-Host "== Логіни застосунків (найменші привілеї)"
Invoke-Sql "security.sql"

Write-Host "== Проєкт №3: FeedbackDb (MongoDB)"
Invoke-Mongo "p3/collections.js"
Invoke-Mongo "p3/seed.js"

Write-Host "== Готово. Перевірки: db/p1/checks.sql, db/p2/checks.sql, db/p3/checks.js" -ForegroundColor Green
