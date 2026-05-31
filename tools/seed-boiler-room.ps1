# Seeds the items needed to COMPLETE the vanilla Community Center "Boiler Room"
# (area 3) into a save's PLAYER INVENTORY, so the #47 missed-rewards-chest repro can
# be set up: finish all 3 Boiler Room bundles in-game, leave a reward ungrabbed, and
# the chest should appear at tile (22,10).
#
# Why the Boiler Room: the player's SVE config (SVECommunityBundles=true,
# HardSVECommunityBundles=false) comments out the Boiler Room bundles, so they fall
# back to VANILLA item requirements — every ingredient is a stock item with an id we
# can inject confidently (no SVE custom-item id strings to get wrong).
#
#   Bundle 20 Blacksmith's : Copper Bar (334), Iron Bar (335), Gold Bar (336)   [all 3]
#   Bundle 21 Geologist's  : Quartz (80), Earth Crystal (86), Frozen Tear (84),
#                            Fire Quartz (82)                                    [all 4]
#   Bundle 22 Adventurer's : needs ANY 2 of its options — Solar Essence (768) +
#                            Void Essence (769) are the easiest                  [2 of 4]
#
# Keeps the player's TOOLS / RINGS / EQUIPMENT (every non-Object item) and clears only
# the Object junk to make room, then appends the bundle items. Compact single-line save
# safe; edits the items node via XmlDocument so unrelated XML can't be corrupted.
#
# Usage:  pwsh -NoProfile -File seed-boiler-room.ps1 -SavePath "<path to save file>"
param(
    [Parameter(Mandatory = $true)][string]$SavePath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $SavePath)) {
    Write-Error "Save not found at $SavePath"
    exit 1
}

$backupPath = "$SavePath.before-boiler-seed"
if (Test-Path -LiteralPath $backupPath) {
    Write-Host "Backup already exists at $backupPath - skipping backup."
} else {
    Copy-Item -LiteralPath $SavePath -Destination $backupPath
    Write-Host "Backup written: $backupPath"
}

# --- item definitions (stack 5 each = margin; bundles take only what they need) ------
# category/type are NOT authoritative: the game's ItemRegistry resolves the donation
# eligibility from the qualified item id ("(O)334" etc.), not these stored fields.
$items = @(
    @{ Name = "Copper Bar";    ItemId = "334"; PSI = 334; Stack = 5; Type = "Basic";    Category = -15; Price = 60;  CanSetDown = "true" }
    @{ Name = "Iron Bar";      ItemId = "335"; PSI = 335; Stack = 5; Type = "Basic";    Category = -15; Price = 120; CanSetDown = "true" }
    @{ Name = "Gold Bar";      ItemId = "336"; PSI = 336; Stack = 5; Type = "Basic";    Category = -15; Price = 250; CanSetDown = "true" }
    @{ Name = "Quartz";        ItemId = "80";  PSI = 80;  Stack = 5; Type = "Minerals"; Category = -2;  Price = 25;  CanSetDown = "true" }
    @{ Name = "Earth Crystal"; ItemId = "86";  PSI = 86;  Stack = 5; Type = "Minerals"; Category = -2;  Price = 50;  CanSetDown = "true" }
    @{ Name = "Frozen Tear";   ItemId = "84";  PSI = 84;  Stack = 5; Type = "Minerals"; Category = -2;  Price = 75;  CanSetDown = "true" }
    @{ Name = "Fire Quartz";   ItemId = "82";  PSI = 82;  Stack = 5; Type = "Minerals"; Category = -2;  Price = 100; CanSetDown = "true" }
    @{ Name = "Solar Essence"; ItemId = "768"; PSI = 768; Stack = 5; Type = "Basic";    Category = -28; Price = 40;  CanSetDown = "true" }
    @{ Name = "Void Essence";  ItemId = "769"; PSI = 769; Stack = 5; Type = "Basic";    Category = -28; Price = 50;  CanSetDown = "true" }
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

# Remove every Object item and every empty slot; keep tools / rings / equipment.
$removedObjects = 0
foreach ($node in @($playerItems.SelectNodes("Item"))) {
    $xsiType = $node.GetAttribute("type", $XSI)
    $isNil = $node.GetAttribute("nil", $XSI) -eq "true"
    if ($isNil) {
        [void]$playerItems.RemoveChild($node)
    } elseif ($xsiType -eq "Object") {
        $nm = $node.SelectSingleNode("name")
        Write-Host "  Cleared Object -> $(if ($nm) { $nm.InnerText } else { '?' })"
        [void]$playerItems.RemoveChild($node)
        $removedObjects++
    }
}
$kept = @($playerItems.SelectNodes("Item")).Count
Write-Host "Kept $kept non-Object items (tools / rings / equipment); cleared $removedObjects Object items + empty slots."

# Append the Boiler Room bundle items.
foreach ($def in $items) {
    $node = Import-Xml -Doc $doc -Xml ((New-ItemXml -Def $def).Trim())
    [void]$playerItems.AppendChild($node)
    Write-Host "  Appended -> $($def.Name) x$($def.Stack)"
}

# Pad with empty slots up to TARGET_CAP, then set maxItems to match.
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
Write-Host "Seeded $($items.Count) Boiler Room bundle items into player inventory."
