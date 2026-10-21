<#
  Reconstruir Cat Squad desde la versión 2.5D estable.

  PASO 1 (preparar):   powershell -ExecutionPolicy Bypass -File .\reconstruir_catsquad.ps1
     - Revisa que Unity esté cerrado.
     - Hace un commit de respaldo del estado actual y crea una rama de respaldo.
     - Crea la rama "reconstruccion" en el commit 8cfe577 (la primera versión 2.5D).
     - Borra Library, Temp y obj (la caché de Unity).
     - Copia el zip completo (Assets/CatSquadRP, VisionBridge, Docs, LEEME) sobre el proyecto, reemplazando archivos.

  Después: abre el proyecto en Unity 6 LTS y prueba las escenas desde el menú CatSquad.

  PASO 2 (guardar):    powershell -ExecutionPolicy Bypass -File .\reconstruir_catsquad.ps1 -Confirmar
     - Hace el commit con todo lo aplicado.

  Parámetros opcionales:
     -Proyecto "C:\ruta\al\proyecto"     (por defecto: la carpeta actual)
     -Zip      "C:\ruta\a\la\carpeta\descomprimida"   (por defecto: la busca en Descargas y Escritorio)
#>
param(
    [string]$Proyecto = (Get-Location).Path,
    [string]$Zip = "",
    [switch]$Confirmar
)

$ErrorActionPreference = "Continue"   # los mensajes de git por stderr no deben cortar el script
$CommitBase = "8cfe577824df1185c02de217a6b051758af54377"
$Fecha = Get-Date -Format "yyyyMMdd-HHmm"

function Ok($t)    { Write-Host "  [OK] $t" -ForegroundColor Green }
function Info($t)  { Write-Host "  $t" -ForegroundColor Cyan }
function Aviso($t) { Write-Host "  [!] $t" -ForegroundColor Yellow }
function Fallo($t) { Write-Host "  [X] $t" -ForegroundColor Red; exit 1 }
function RunGit { & git @args; if ($LASTEXITCODE -ne 0) { Fallo "git $($args -join ' ') falló" } }

Write-Host ""
Write-Host "=== Cat Squad: reconstrucción desde la versión 2.5D ===" -ForegroundColor Magenta

# ---------------------------------------------------------------- proyecto
if (-not (Test-Path (Join-Path $Proyecto "Assets"))) {
    $Proyecto = Read-Host "No encuentro la carpeta Assets aquí. Pega la ruta del proyecto de Unity"
    $Proyecto = $Proyecto.Trim('"')
}
if (-not (Test-Path (Join-Path $Proyecto "Assets"))) { Fallo "Esa carpeta no es un proyecto de Unity (no tiene Assets)." }
Set-Location $Proyecto
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Fallo "No encuentro git. Instálalo o abre esta ventana desde Git Bash/GitHub Desktop." }
& git rev-parse --is-inside-work-tree *> $null
if ($LASTEXITCODE -ne 0) { Fallo "Esta carpeta no es un repositorio git." }
Ok "Proyecto: $Proyecto"

# ---------------------------------------------------------------- PASO 2: confirmar
if ($Confirmar) {
    $rama = (& git rev-parse --abbrev-ref HEAD).Trim()
    Info "Rama actual: $rama"
    RunGit add -A
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { Aviso "No hay cambios para guardar."; exit 0 }
    RunGit commit -m "Reconstrucción: Etapas 1-4 + Lab 05 (trayectoria) + Lab 06 (waypoints) sobre la versión 2.5D"
    Ok "Commit hecho en la rama '$rama'."
    Info "Para subirlo a GitHub:  git push -u origin $rama"
    exit 0
}

# ---------------------------------------------------------------- Unity cerrado
if (Get-Process -Name "Unity" -ErrorAction SilentlyContinue) {
    Fallo "Unity está abierto. Ciérralo (y Unity Hub si tiene el proyecto abierto) y vuelve a ejecutar."
}
Ok "Unity está cerrado"

# ---------------------------------------------------------------- carpeta del zip
function Es-Zip($p) { return ($p -and (Test-Path (Join-Path $p "Assets\CatSquadRP\Scripts")) -and (Test-Path (Join-Path $p "Assets\CatSquadRP\Lab06"))) }
if (-not (Es-Zip $Zip)) {
    $buscar = @("$HOME\Downloads", "$HOME\Descargas", "$HOME\Desktop", "$HOME\Escritorio", "$HOME\OneDrive\Desktop", "$HOME\OneDrive\Escritorio")
    foreach ($b in $buscar) {
        if (-not (Test-Path $b)) { continue }
        $cand = Get-ChildItem $b -Directory -Filter "CatSquad_RescateProfundo_Completo*" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
        foreach ($c in $cand) {
            if (Es-Zip $c.FullName) { $Zip = $c.FullName; break }
            $sub = Get-ChildItem $c.FullName -Directory -ErrorAction SilentlyContinue | Where-Object { Es-Zip $_.FullName } | Select-Object -First 1
            if ($sub) { $Zip = $sub.FullName; break }
        }
        if (Es-Zip $Zip) { break }
    }
}
if (-not (Es-Zip $Zip)) {
    $Zip = (Read-Host "Pega la ruta de la carpeta donde descomprimiste CatSquad_RescateProfundo_Completo.zip").Trim('"')
}
if (-not (Es-Zip $Zip)) { Fallo "En esa carpeta no está Assets\CatSquadRP con Lab06. Revisa la ruta." }
if ((Resolve-Path $Zip).Path.StartsWith((Resolve-Path $Proyecto).Path)) {
    Fallo "Descomprimiste el zip DENTRO del proyecto. Muévelo a Descargas (fuera del proyecto) y vuelve a ejecutar."
}
Ok "Zip descomprimido: $Zip"

# ---------------------------------------------------------------- respaldo
Info "Guardando un respaldo del estado actual..."
RunGit add -A
& git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    RunGit commit -m "Respaldo antes del downgrade ($Fecha)"
    Ok "Commit de respaldo creado"
} else {
    Ok "No había cambios sin guardar"
}
$respaldo = "respaldo-antes-downgrade-$Fecha"
RunGit branch $respaldo
Ok "Rama de respaldo: $respaldo  (ahí queda todo lo anterior, incluido lo de Claudia)"

# ---------------------------------------------------------------- volver a la 2.5D
& git cat-file -e "$CommitBase^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) {
    Info "No tengo ese commit en local, lo bajo de GitHub..."
    RunGit fetch --all
    & git cat-file -e "$CommitBase^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) { Fallo "No existe el commit $CommitBase en este repositorio." }
}
$rama = "reconstruccion"
& git show-ref --verify --quiet "refs/heads/$rama"
if ($LASTEXITCODE -eq 0) { $rama = "reconstruccion-$Fecha" }
RunGit checkout -b $rama $CommitBase
Ok "Rama '$rama' creada en la versión 2.5D ($($CommitBase.Substring(0,7)))"

# ---------------------------------------------------------------- caché de Unity
foreach ($d in @("Library", "Temp", "obj")) {
    $p = Join-Path $Proyecto $d
    if (Test-Path $p) { Remove-Item $p -Recurse -Force; Ok "Borrada la carpeta $d" }
}

# ---------------------------------------------------------------- aplicar el zip (reemplazando, sin borrar la carpeta)
Info "Copiando el proyecto completo sobre la versión 2.5D..."
function Copiar($origen, $destino) {
    if (-not (Test-Path $origen)) { return }
    & robocopy $origen $destino /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fallo "robocopy falló copiando $origen" }
    Ok "Copiado: $(Split-Path $origen -Leaf)"
}
Copiar (Join-Path $Zip "Assets\CatSquadRP") (Join-Path $Proyecto "Assets\CatSquadRP")
Copiar (Join-Path $Zip "VisionBridge") (Join-Path $Proyecto "VisionBridge")
Copiar (Join-Path $Zip "Docs") (Join-Path $Proyecto "Docs")
foreach ($f in @("LEEME.md", "LEEME_Lab06.md")) {
    if (Test-Path (Join-Path $Zip $f)) { Copy-Item (Join-Path $Zip $f) $Proyecto -Force; Ok "Copiado: $f" }
}

# ---------------------------------------------------------------- versión de Unity
$pv = Join-Path $Proyecto "ProjectSettings\ProjectVersion.txt"
$ver = if (Test-Path $pv) { ((Get-Content $pv | Select-String "m_EditorVersion:" | Select-Object -First 1).Line.Split(":")[1].Trim()) } else { "?" }

Write-Host ""
Write-Host "=== Listo el PASO 1 ===" -ForegroundColor Magenta
Info "Este commit se hizo con Unity $ver. Ábrelo con esa versión o con Unity 6 LTS (6000.0.x). NO con la 6.3 Alpha."
Info "En Unity, prueba desde el menú CatSquad:"
Info "   1. Construir escena Rescate Profundo (2.5D)"
Info "   2. Construir escena 3D (primera persona)   <- aquí está el lanzador del Lab 05"
Info "   3. Lab 05: Construir minijuego 2D (El Muelle)"
Info "   4. Lab 06: Escena terminada"
Info "Si algo sale magenta: CatSquad -> Arreglar materiales magenta (URP)"
Write-Host ""
Info "Cuando todo funcione, cierra Unity y ejecuta:"
Write-Host "   powershell -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Confirmar" -ForegroundColor White
Write-Host ""
