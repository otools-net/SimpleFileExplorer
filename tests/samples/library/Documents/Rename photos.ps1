# Renames the photos of a folder after the day they were taken: 2025-07-14 001.jpg, 2025-07-14 002.jpg...
param(
    [Parameter(Mandatory)] [string] $Folder,
    [switch] $WhatIf
)

$count = @{}
Get-ChildItem -Path $Folder -Filter *.jpg | Sort-Object LastWriteTime | ForEach-Object {
    $day = $_.LastWriteTime.ToString('yyyy-MM-dd')
    $count[$day] = 1 + $count[$day]
    $name = '{0} {1:000}{2}' -f $day, $count[$day], $_.Extension.ToLower()
    if ($_.Name -ne $name) {
        Rename-Item -LiteralPath $_.FullName -NewName $name -WhatIf:$WhatIf
    }
}
Write-Host "Renamed $($count.Values | Measure-Object -Sum | Select-Object -ExpandProperty Sum) photos"
