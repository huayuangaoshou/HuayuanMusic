param(
    [string]$Exe = 'D:\AI\Projects\HuayuanMusic\app-m223\artifacts\release-build\app\HuayuanMusic.P17.Nightflight.exe'
)
# R18b 防砖自测：坏库一(user_version=9) + 坏库二(头部512乱数据) + 好库回归。
# 前置：关闭正在运行的 HuayuanMusic，否则单实例互斥会让新实例直接退出。
# 安全：所有操作先备份真实库；隔离动作是"改名"（绝不删除）；测完自动还原真实库。

$ErrorActionPreference = 'Stop'
$db = Join-Path $env:APPDATA 'HuayuanMusic-M223\huayuan-music-p1.db'
$logRoot = Join-Path $env:LOCALAPPDATA 'HuayuanMusic-M223\logs'
$backupDir = Join-Path $env:TEMP ("hm-r18b-backup-" + (Get-Date -Format 'yyyyMMddHHmmss'))
$stamp = Get-Date -Format 'yyyyMMddHHmmss'

function Backup-Db {
    $null = New-Item -ItemType Directory -Force -Path $backupDir
    foreach ($suffix in @('', '-wal', '-shm')) {
        $p = $db + $suffix
        if (Test-Path -LiteralPath $p) {
            Copy-Item -LiteralPath $p -Destination (Join-Path $backupDir (Split-Path $p -Leaf))
        }
    }
    Write-Host "已备份真实库到: $backupDir" -ForegroundColor Cyan
}

function Restore-Db {
    if (Test-Path -LiteralPath $backupDir) {
        # 清掉测试产生的隔离/新库（改名保留现场，不删除）
        foreach ($p in @($db, $db + '-wal', $db + '-shm')) {
            if (Test-Path -LiteralPath $p) {
                Move-Item -LiteralPath $p -Destination ($p + '.test-' + $stamp) -Force
            }
        }
        Get-ChildItem -LiteralPath $backupDir | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path (Split-Path $db) $_.Name)
        }
        Write-Host "已还原真实库。" -ForegroundColor Green
    }
}

function Get-LatestLog {
    Get-ChildItem -LiteralPath $logRoot -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

function Launch-And-Wait {
    param([int]$WaitSeconds = 8)
    $p = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Seconds $WaitSeconds
    # 桌面程序带托盘，可能不自动退出；测试只关心启动是否成功 + 日志，手动/自动结束实例
    if (-not $p.HasExited) { $p | Stop-Process -Force; Start-Sleep -Seconds 1 }
}

Write-Host "==> 前置：确保无 HuayuanMusic 运行中" -ForegroundColor Yellow
if (Get-Process -Name 'HuayuanMusic*' -ErrorAction SilentlyContinue) {
    throw "检测到 HuayuanMusic 仍在运行，请先彻底退出（托盘右键→彻底退出）再执行本脚本。"
}

Backup-Db

try {
    # ---------- 好库回归 ----------
    Write-Host "`n==> [回归] 正常库启动" -ForegroundColor Cyan
    $before = Get-LatestLog
    Launch-And-Wait
    $after = Get-LatestLog
    Write-Host "  最新日志会话: $($after.Name)"

    # ---------- 坏库一：user_version=9 ----------
    Write-Host "`n==> [坏库一] PRAGMA user_version = 9" -ForegroundColor Cyan
    $conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$db")
    # 用纯文件方式改 user_version 更稳妥：直接用 SQLite CLI 不可用时，用 System.Data 旁路
    # 这里用 PowerShell 直接改第 18 字节起的 4 字节大端整数(user_version 位于偏移 60)
    # 更简单：调用 sqlite3 若存在
    $sqlite3 = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if ($sqlite3) {
        & sqlite3 $db "PRAGMA user_version=9;"
    } else {
        # 无 sqlite3：用 .NET 改
        Add-Type -AssemblyName System.Data
        $conn2 = New-Object System.Data.SQLite.SQLiteConnection 2>$null
        # 回退：直接改写头偏移(主数据库 header 的 user_version 在偏移 60，4 字节 big-endian)
        $bytes = [System.IO.File]::ReadAllBytes($db)
        $bytes[60]=0; $bytes[61]=0; $bytes[62]=0; $bytes[63]=9
        [System.IO.File]::WriteAllBytes($db, $bytes)
    }
    Launch-AndWait
    $after1 = Get-LatestLog
    Write-Host "  会话: $($after1.Name)"

    # ---------- 坏库二：头部 512 乱数据 ----------
    Write-Host "`n==> [坏库二] 头部 512 字节覆写乱数据" -ForegroundColor Cyan
    $bytes = [System.IO.File]::ReadAllBytes($db)
    $rng = [System.Random]::new()
    for ($i = 0; $i -lt 512; $i++) { $bytes[$i] = [byte]$rng.Next(0, 256) }
    [System.IO.File]::WriteAllBytes($db, $bytes)
    Launch-AndWait
    $after2 = Get-LatestLog
    Write-Host "  会话: $($after2.Name)"

    # ---------- 回报日志行 ----------
    Write-Host "`n==> 关键日志行（STARTUP_DB_QUARANTINED / STARTUP_RECOVERED）" -ForegroundColor Cyan
    foreach ($s in @($after1, $after2)) {
        $f = Join-Path $s.FullName 'events.jsonl'
        if (Test-Path -LiteralPath $f) {
            Select-String -LiteralPath $f -Pattern 'STARTUP_DB_QUARANTINED|STARTUP_RECOVERED|STARTUP_DB_QUARANTINE_PARTIAL' |
                ForEach-Object { $_.Line }
        }
    }
}
finally {
    Restore-Db
}
Write-Host "`n完成。备份目录: $backupDir" -ForegroundColor Green
