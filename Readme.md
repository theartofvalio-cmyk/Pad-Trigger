<p align="center">
  <img src="pt.png" width="180" alt="Pad Trigger icon">
</p>

<h1 align="center">Pad Trigger</h1>

<p align="center">
  A lightweight Windows tray tool for triggering actions when controllers connect or disconnect.
</p>

---

## What is Pad Trigger?

Pad Trigger is a lightweight Windows tray tool that lets you run custom actions or scripts when a game controller connects or disconnects from your computer.

It is useful for setups where you want your PC to automatically react when a specific controller is turned on or off.

For example, you can use it to:

- Switch Windows to TV mode when a controller connects
- Launch a game launcher like Playnite
- Change audio devices
- Run batch files, PowerShell scripts, VBS scripts, or EXE files
- Return your PC back to desktop mode when the controller disconnects

---

## Features

### Controller detection

Pad Trigger detects connected game controllers and shows them in a simple list.

Each controller has:

- Controller name
- Connection status
- Custom actions for connect
- Custom actions for disconnect

You can rename controllers inside the app, so instead of confusing device names, you can use names like:

- Xbox Controller
- 8BitDo Controller
- HOTAS Joystick
- Racing Wheel
- TV Mode Controller

---

### Connect actions

You can assign one or more actions to run when a controller connects.

Actions run from top to bottom.

Example connect actions:

```text
D:\Scripts\TV_Mode.bat
D:\Scripts\Launch_Playnite.bat
D:\Tools\SomeProgram.exe
```

---

### Disconnect actions

You can also assign one or more actions to run when a controller disconnects.

Example disconnect actions:

```text
D:\Scripts\Desktop_Mode.bat
D:\Scripts\Restore_Audio.bat
```

---

### Multiple actions per controller

Each controller can have multiple connect and disconnect actions.

For example:

When controller connects:

1. Turn on TV
2. Switch Windows display mode
3. Change audio output
4. Start Playnite

When controller disconnects:

1. Switch back to desktop monitor
2. Restore desktop audio
3. Turn off TV

---

### Runs from the system tray

Pad Trigger is designed to stay out of the way.

When you close the main window, it keeps running in the Windows system tray near the clock.

From the tray icon, you can:

- Open Pad Trigger
- Open the About window
- Exit the app completely

---

### Start with Windows minimized

Pad Trigger can start automatically with Windows.

When enabled, it starts in the background and stays in the tray.

Important: Pad Trigger does not run disconnect actions just because Windows started and a controller is already disconnected.

It only reacts after a controller actually connects, then later disconnects.

---

### Light and dark theme

Pad Trigger includes both light and dark themes.

The app starts in dark theme by default. Users can switch to light theme whenever they want.

The selected theme is remembered automatically.

---

## How it works

Pad Trigger watches for controller device changes in Windows and also checks controller state efficiently in the background.

When a controller is detected as connected, Pad Trigger checks if that controller was previously disconnected.

If yes, it runs the controller's connect actions.

When the controller later disappears or disconnects, Pad Trigger runs that controller's disconnect actions.

Each controller has its own saved configuration.

---

## Supported action types

Pad Trigger can run common Windows files, including:

```text
.exe
.bat
.cmd
.ps1
.vbs
```

PowerShell scripts are launched with:

```text
-NoProfile -ExecutionPolicy Bypass
```

This helps normal user scripts run without needing to manually change PowerShell execution policy.

---

## Configuration

Pad Trigger creates a local configuration file named:

```text
config.json
```

This file stores:

- Controller names
- Controller actions
- Theme setting
- Saved controller profiles

Do not upload your personal `config.json` publicly if it contains private paths or personal scripts.

---

## Example use case

A common use case is a living-room gaming PC setup.

Example:

When an Xbox controller connects:

```text
D:\Scripts\TV_Mode.bat
D:\Scripts\Launch_Playnite.bat
```

When the Xbox controller disconnects:

```text
D:\Scripts\Desktop_Mode.bat
```

This allows the controller to act like a trigger for switching between desktop mode and TV gaming mode.

---

## Installation

Download the latest release from the Releases section.

Run:

```text
PadTrigger.exe
```

No installation is required.

---

## Requirements

- Windows 10 or Windows 11
- Game controller supported by Windows

The release build is intended to be self-contained, so normal users should not need to install Visual Studio or the .NET SDK.

---

## Building from source

To build Pad Trigger yourself:

1. Install Visual Studio
2. Install the `.NET desktop development` workload
3. Open the solution file
4. Restore NuGet packages
5. Build the project

The project uses:

```text
.NET 8
Windows Forms
SharpDX.DirectInput
```

---

## Project files

Important project files:

```text
Form1.cs
Program.cs
PadTrigger.csproj
pt.ico
pt.png
```

`pt.ico` is used for the Windows executable icon.

`pt.png` is embedded in the app and used for the large in-app icon.

---

## Made by

Tool made by **Valentin Yochev**.

YouTube channel:

https://www.youtube.com/@SPYBGWTVR

---

## Support

If you like this tool and want to help me out, subscribe to my YouTube channel:

https://www.youtube.com/@SPYBGWTVR?sub_confirmation=1

That helps more than any donation. Thanks!

---

## License

This project is free and open source.

You are free to use, modify, and share it.
