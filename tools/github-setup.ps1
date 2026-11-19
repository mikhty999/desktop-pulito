# Pubblica Desktop Pulito su GitHub: repository, tutti i branch, etichette, issue e bacheca (To do / Working / Closed).
# Requisiti: GitHub CLI (winget install GitHub.cli) e accesso: gh auth login  (+ gh auth refresh -s project per la bacheca)
# Uso:  powershell -ExecutionPolicy Bypass -File tools\github-setup.ps1 -Repo desktop-pulito [-Private]
param([string]$Repo = 'desktop-pulito', [switch]$Private)
$ErrorActionPreference = 'Continue'
Set-Location (Split-Path $PSScriptRoot)

gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE) { Write-Host 'Prima collegati: gh auth login' -ForegroundColor Yellow; exit 1 }
$owner = gh api user --jq .login

# 1) repository + tutti i branch
$exists = gh repo view "$owner/$Repo" 2>$null
if (-not $exists) {
    $vis = if ($Private) { '--private' } else { '--public' }
    gh repo create "$owner/$Repo" $vis --description 'Customize your Windows desktop: liquid-glass UI, stunning transitions, scenes. Free, open source, ultra light.' --source . --remote origin
}
git push -u origin --all
gh repo edit "$owner/$Repo" --add-topic windows --add-topic desktop --add-topic customization --add-topic liquid-glass --add-topic dotnet --add-topic clean-architecture

# 2) etichette di stato e di area
$labels = @{
    'to do' = 'd4c5f9'; 'working' = 'fbca04'
    'architecture' = '5319e7'; 'domain' = '0e8a16'; 'infrastructure' = '1d76db'; 'ui' = 'c5def5'
    'gpu' = 'b60205'; 'gallery' = 'f9d0c4'; 'release' = '006b75'
}
foreach ($k in $labels.Keys) { gh label create $k --color $labels[$k] --force --repo "$owner/$Repo" | Out-Null }

# 3) bacheca (Project) con colonne To do / Working / Closed
$projNum = $null
$proj = gh project create --owner $owner --title 'Desktop Pulito' --format json 2>$null | ConvertFrom-Json
if ($proj) {
    $projNum = $proj.number
    gh project link $projNum --owner $owner --repo "$owner/$Repo" | Out-Null
    # rinomina le opzioni del campo Status: Todo → To do, In Progress → Working, Done → Closed
    $field = gh project field-list $projNum --owner $owner --format json | ConvertFrom-Json | % fields | ? { $_.name -eq 'Status' }
    $q = 'mutation($f:ID!){updateProjectV2Field(input:{fieldId:$f,singleSelectOptions:[{name:"To do",color:GRAY,description:""},{name:"Working",color:YELLOW,description:""},{name:"Closed",color:GREEN,description:""}]}){projectV2Field{... on ProjectV2SingleSelectField{options{id name}}}}}'
    $opts = (gh api graphql -f query=$q -f f=$($field.id) | ConvertFrom-Json).data.updateProjectV2Field.projectV2Field.options
    $optId = @{}; foreach ($o in $opts) { $optId[$o.name] = $o.id }
} else { Write-Host 'Bacheca saltata (serve: gh auth refresh -s project)' -ForegroundColor Yellow }

# 4) issue
$issues = Get-Content tools\issues.json -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($it in $issues) {
    $lab = @($it.area); if ($it.status -eq 'todo') { $lab += 'to do' } elseif ($it.status -eq 'working') { $lab += 'working' }
    $url = gh issue create --repo "$owner/$Repo" --title $it.title --body $it.body --label ($lab -join ',')
    if ($it.status -eq 'closed') { gh issue close $url --repo "$owner/$Repo" --reason completed | Out-Null }
    if ($projNum) {
        $item = gh project item-add $projNum --owner $owner --url $url --format json | ConvertFrom-Json
        $col = @{ todo = 'To do'; working = 'Working'; closed = 'Closed' }[$it.status]
        gh project item-edit --id $item.id --project-id $proj.id --field-id $field.id --single-select-option-id $optId[$col] | Out-Null
    }
    Write-Host ("{0,-8} {1}" -f $it.status, $it.title)
}
Write-Host "`nFatto: https://github.com/$owner/$Repo" -ForegroundColor Green
