# Seeds the items needed to reproduce #71 (book-read-as-skill bug) into a save's
# PLAYER INVENTORY by replacing empty slots:
#   5x Bomb (287)          — to blow the boulder blocking the Dwarf in the Mines
#   2x Mega Bomb (288)     — spare, larger blast
#   1x Dwarf Scroll I  (96)
#   1x Dwarf Scroll II (97)
#   1x Dwarf Scroll III(98)
#   1x Dwarf Scroll IV (99) — donate all 4 to the Museum so Gunther hands over
#                             the Dwarvish Translation Guide (the #71 repro item)
#
# Works on the COMPACT single-line save the game writes on-device (the geode
# seed script assumed a pretty-printed multi-line save and splices by line
# number — that does NOT work here). Uses XmlDocument so it edits the player
# items node precisely and can't corrupt unrelated XML.
#
# Usage:  pwsh -NoProfile -File seed-dwarf-test.ps1 -SavePath "<path to save file>"
param(
    [Parameter(Mandatory = $true)][string]$SavePath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $SavePath)) {
    Write-Error "Save not found at $SavePath"
    exit 1
}

$backupPath = "$SavePath.before-dwarf-seed"
if (Test-Path -LiteralPath $backupPath) {
    Write-Host "Backup already exists at $backupPath - skipping backup."
} else {
    Copy-Item -LiteralPath $SavePath -Destination $backupPath
    Write-Host "Backup written: $backupPath"
}

# --- item definitions ---------------------------------------------------------
# category/type are NOT authoritative: the game's ItemRegistry resolves them from
# the qualified item id when the item is first touched, and both the bomb
# placement (placementAction switch on "(O)287"/"(O)288") and the museum donation
# check (IsItemSuitableForDonation -> data lookup by QualifiedItemId) key off the
# id, not these stored fields. They are set to plausible values for cleanliness.
$items = @(
    @{ Name = "Bomb";           ItemId = "287"; PSI = 287; Stack = 5; Type = "Crafting"; Category = -8;  Price = 50; CanSetDown = "true" }
    @{ Name = "Mega Bomb";      ItemId = "288"; PSI = 288; Stack = 2; Type = "Crafting"; Category = -8;  Price = 50; CanSetDown = "true" }
    @{ Name = "Dwarf Scroll I"; ItemId = "96";  PSI = 96;  Stack = 1; Type = "Arch";     Category = -28; Price = 1;  CanSetDown = "true" }
    @{ Name = "Dwarf Scroll II";ItemId = "97";  PSI = 97;  Stack = 1; Type = "Arch";     Category = -28; Price = 1;  CanSetDown = "true" }
    @{ Name = "Dwarf Scroll III";ItemId = "98"; PSI = 98;  Stack = 1; Type = "Arch";     Category = -28; Price = 1;  CanSetDown = "true" }
    @{ Name = "Dwarf Scroll IV";ItemId = "99";  PSI = 99;  Stack = 1; Type = "Arch";     Category = -28; Price = 1;  CanSetDown = "true" }
)

function New-ItemXml {
    param($Def)
    @"
<Item xsi:type="Object"><isLostItem>false</isLostItem><category>$($Def.Category)</category><hasBeenInInventory>true</hasBeenInInventory><name>$($Def.Name)</name><parentSheetIndex>$($Def.PSI)</parentSheetIndex><itemId>$($Def.ItemId)</itemId><specialItem>false</specialItem><isRecipe>false</isRecipe><quality>0</quality><stack>$($Def.Stack)</stack><SpecialVariable>0</SpecialVariable><tileLocation><X>0</X><Y>0</Y></tileLocation><owner>0</owner><type>$($Def.Type)</type><canBeSetDown>$($Def.CanSetDown)</canBeSetDown><canBeGrabbed>true</canBeGrabbed><isSpawnedObject>false</isSpawnedObject><questItem>false</questItem><isOn>true</isOn><fragility>0</fragility><price>$($Def.Price)</price><edibility>-300</edibility><bigCraftable>false</bigCraftable><setOutdoors>false</setOutdoors><setIndoors>false</setIndoors><readyForHarvest>false</readyForHarvest><showNextIndex>false</showNextIndex><flipped>false</flipped><isLamp>false</isLamp><minutesUntilReady>0</minutesUntilReady><boundingBox><X>0</X><Y>0</Y><Width>64</Width><Height>64</Height><Location><X>0</X><Y>0</Y></Location><Size><X>64</X><Y>64</Y></Size></boundingBox><scale><X>0</X><Y>0</Y></scale><uses>0</uses><destroyOvernight>false</destroyOvernight></Item>
"@
}

# --- load + locate player inventory ------------------------------------------
$XSI = "http://www.w3.org/2001/XMLSchema-instance"
$TARGET_CAP = 36   # premium backpack — valid vanilla inventory size

function Import-Xml {
    param($Doc, [string]$Xml)
    $wrap = "<root xmlns:xsi='$XSI' xmlns:xsd='http://www.w3.org/2001/XMLSchema'>$Xml</root>"
    $tmp = New-Object System.Xml.XmlDocument
    $tmp.LoadXml($wrap)
    return $Doc.ImportNode($tmp.DocumentElement.FirstChild, $true)
}

$doc = New-Object System.Xml.XmlDocument
$doc.PreserveWhitespace = $true
$doc.Load($SavePath)

$playerItems = $doc.SelectSingleNode("/SaveGame/player/items")
if ($null -eq $playerItems) {
    Write-Error "Could not find /SaveGame/player/items in save."
    exit 1
}

$existing = @($playerItems.SelectNodes("Item")).Count
Write-Host "Player inventory currently has $existing slots."

# Append the seeded items at the end of the inventory list.
foreach ($def in $items) {
    $node = Import-Xml -Doc $doc -Xml ((New-ItemXml -Def $def).Trim())
    [void]$playerItems.AppendChild($node)
    Write-Host "  Appended -> $($def.Name) x$($def.Stack)"
}

# Pad with empty slots up to TARGET_CAP, then set maxItems to match so the
# inventory list length == backpack capacity (Farmer.Items is capacity-bound).
$count = @($playerItems.SelectNodes("Item")).Count
while ($count -lt $TARGET_CAP) {
    [void]$playerItems.AppendChild((Import-Xml -Doc $doc -Xml '<Item xsi:nil="true" />'))
    $count++
}

$maxItems = $doc.SelectSingleNode("/SaveGame/player/maxItems")
if ($null -ne $maxItems) {
    $old = $maxItems.InnerText
    $maxItems.InnerText = "$TARGET_CAP"
    Write-Host "maxItems: $old -> $TARGET_CAP"
}

Write-Host "Final inventory slot count: $count"
$doc.Save($SavePath)
Write-Host "Save written: $SavePath"
Write-Host "Seeded $($items.Count) items (5 Bomb, 2 Mega Bomb, 4 Dwarf Scrolls) into player inventory."
