# Controller Compatibility Matrix

This document tracks controller testing across different Android devices.

## Test Devices

| Device | OS | Notes |
|--------|-----|-------|
| AYN Odin Pro | Android | Primary test device |
| NXTPaper 11 Plus | Android | Tablet, title screen UI mispositioned (SMAPI/GMCM — not our bug) |

## Controller Test Results

### AYN Odin Pro

| Controller | Connection | Status | Notes |
|------------|------------|--------|-------|
| **Built-in (Odin)** | N/A | ✅ Working | All buttons and triggers work |
| **Xbox One Wireless** | Bluetooth | ✅ Working | Triggers not detected - enable "Use Bumpers Instead of Triggers" |
| **Xbox Series X\|S Wireless** | Bluetooth | ✅ Working | Triggers not detected - enable "Use Bumpers Instead of Triggers" |
| **Nintendo Switch Pro Controller** | Bluetooth | ❌ Cannot Test | Will not pair with Odin |
| **PlayStation DualShock 4 (PS4)** | Bluetooth | ⏳ To Test | |
| **PlayStation DualSense (PS5)** | Bluetooth | ⏳ To Test | |

### NXTPaper 11 Plus

| Controller | Connection | Status | Notes |
|------------|------------|--------|-------|
| **EasySMX S10** | Bluetooth | ⚠️ Partial | Buttons reported reversed — see Known Issues below |

### 3rd Party Controllers (To Be Added)

| Controller | Connection | Status | Notes |
|------------|------------|--------|-------|
| | | | |

## Status Legend

- ✅ Working - Fully functional (with noted workarounds if any)
- ⚠️ Partial - Some features work, some don't
- ❌ Not Working - Does not function with the mod
- ❌ Cannot Test - Cannot pair/connect to device
- ⏳ To Test - Not yet tested

## Known Issues by Controller Type

### Xbox Controllers (Bluetooth)
- Analog triggers (LT/RT) report on different axes (`AXIS_GAS`/`AXIS_BRAKE`) than what Android/Stardew expects (`AXIS_LTRIGGER`/`AXIS_RTRIGGER`)
- **Workaround:** Enable "Use Bumpers Instead of Triggers" in mod settings

### Nintendo Switch Pro Controller
- Pairing issues with AYN Odin Pro - needs testing on other Android devices

### EasySMX S10 (NXTPaper 11 Plus)
- Physical buttons are in Switch layout, but the controller reports them reversed (B=confirm, A=cancel, X/Y swapped)
- Occurs with Switch layout + Switch style selected in mod settings
- Likely the controller is reporting Xbox-style button codes despite having Switch-style physical labels
- Controller has X mode and S mode — switching between them does NOT change the button reversal
- Triggers work in S mode but NOT in X mode
- **Workaround:** Try Xbox layout or Xbox style to compensate
- **Only confirmed on:** NXTPaper 11 Plus — may be device-specific or controller-specific

## Future Test Devices

- Other Android phones/tablets
- Other Android gaming handhelds (ROG Ally, Retroid, etc.)
