<div align="center">

  <img src="MultronWinCleaner/Assets/mwc_logo.png" alt="Multron Win Cleaner Logo" width="130" />

  # Multron Win Cleaner

  **Modern, lightweight, and community-driven Windows system optimizer.**  
  Completely free, open-source, and free of subscriptions, paywalls, or background telemetry.

  <p align="center">
    <a href="https://github.com/Multron-Community/MultronWcleaner/releases">
      <img src="https://img.shields.io/github/v/release/Multron-Community/MultronWcleaner?style=for-the-badge&color=0e7490&label=Release" alt="Release" />
    </a>
    <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078d4?style=for-the-badge&logo=windows&logoColor=white" alt="Platform" />
    <img src="https://img.shields.io/badge/Stack-C%23%20%7C%20WPF-512bd4?style=for-the-badge&logo=dotnet&logoColor=white" alt="Stack" />
    <a href="https://github.com/Multron-Community/MultronWcleaner-Database">
      <img src="https://img.shields.io/badge/Database-Community%20Driven-059669?style=for-the-badge&logo=github&logoColor=white" alt="Database Repo" />
    </a>
    <a href="LICENSE">
      <img src="https://img.shields.io/badge/License-MIT-15803d?style=for-the-badge" alt="License: MIT" />
    </a>
    <a href="https://github.com/Multron-Community/MultronWcleaner/actions/workflows/build.yml">
    <img src="https://img.shields.io/github/actions/workflow/status/Multron-Community/MultronWcleaner/build.yml?branch=beta&style=for-the-badge&logo=githubactions&logoColor=white" alt="Build Status"/>
  </a>
  </p>

  [Download Release](https://github.com/Multron-Community/MultronWcleaner/releases/latest) • [Database Repo](https://github.com/Multron-Community/MultronWcleaner-Database) • [Report Issue](https://github.com/Multron-Community/MultronWcleaner/issues) • [Support on Patreon](https://www.patreon.com/cw/multron)

</div>

> [!WARNING]
> **Antivirus false positives:** some antivirus programs may flag Multron Win Cleaner or one of its DLL files as suspicious when you download or run it. In particular, **Bitdefender** and engines that use it (for example Emsisoft, eScan, GData, Arcabit, VIPRE) may detect `MultronWinCleaner.dll` **heuristically as ransomware**, with a name such as `Gen:Heur.Ransom.Imps.3`. This is a false positive: a heuristic detection is based on behavior, not on a known malware signature, and it is triggered by what a system cleaner has to do (closing programs, deleting files, changing Windows settings). Multron Win Cleaner does not encrypt files, does not ask for a ransom and does not send your data anywhere except the optional cloud malware scan. The file has been reported to Bitdefender as a false positive again. The full source code is in this repository, and every release is built automatically by GitHub Actions. If your antivirus blocks the app, you can report the file to its vendor as a false positive or add an exception for the app folder.
>
> * Bitdefender false positive form: <https://www.bitdefender.com/consumer/support/answer/29358/>
> * Microsoft Defender file submission: <https://www.microsoft.com/en-us/wdsi/filesubmission>

---

## Previews

<div align="center">
  <img width="788" alt="Multron Win Cleaner main window in dark mode with the simplified interface" src="docs/screenshots/main-dark.png" />
</div>

<table>
  <tr>
    <td align="center"><img width="390" alt="Main window in light mode" src="docs/screenshots/main-light.png" /><br/><b>Light mode</b></td>
    <td align="center"><img width="390" alt="Multron Malware Scan" src="docs/screenshots/malware-scan.png" /><br/><b>Multron Malware Scan</b></td>
  </tr>
  <tr>
    <td align="center"><img width="390" alt="Utilities" src="docs/screenshots/utilities.png" /><br/><b>Utilities</b></td>
    <td align="center"><img width="390" alt="Settings" src="docs/screenshots/settings.png" /><br/><b>Settings</b></td>
  </tr>
  <tr>
    <td align="center"><img width="390" alt="Startup Manager" src="docs/screenshots/startup-manager.png" /><br/><b>Startup Manager</b></td>
    <td align="center"><img width="390" alt="Memory Cleaner" src="docs/screenshots/memory-cleaner.png" /><br/><b>Memory Cleaner</b></td>
  </tr>
  <tr>
    <td align="center"><img width="390" alt="Duplicate File Finder" src="docs/screenshots/duplicate-file-finder.png" /><br/><b>Duplicate File Finder</b></td>
    <td align="center"><img width="390" alt="Large File Finder" src="docs/screenshots/large-file-finder.png" /><br/><b>Large File Finder</b></td>
  </tr>
</table>

---

## Overview

**Multron Win Cleaner** is a comprehensive system maintenance, security and disk recovery suite engineered for Windows. Unlike conventional cleaning tools, it pairs deep low-level system controls (DISM, SFC, WinSxS, boot-time operations) with transparent, community-updatable cleaning definitions — and one scan checks junk files, system health, firewall rules, shortcuts, security settings and duplicate files together.

The application is fully localized into **12 languages**: English, Turkish, German, Spanish, French, Italian, Brazilian Portuguese, Russian, Japanese, Korean, Simplified Chinese, and Traditional Chinese.

---

## Key Features

### 🧹 Deep System Cleanup
* **Application Rules:** Continuously updated definitions for third-party software, browser caches, and temporary data.
* **Modular Rule Database:** Cleanup targets are governed by a modular [`database.txt`](https://github.com/Multron-Community/MultronWcleaner-Database) repository, allowing instant community contributions without altering core application binaries.
* **Deep Log Tracing:** Configurable detection and wiping of `.log`, `.etl`, `.dmp`, `.tmp`, and `.bak` files across target directories.
* **Age-Based File Filters:** Target files strictly older than defined day thresholds based on file creation or last access timestamps.
* **Exceptions:** Exclude files and whole folders from scanning and cleaning.
* **Saved Selections:** Your ticked locations and commands are remembered, with a one-click **Reset Selections** button.
* **Native cleanmgr Access:** Windows Disk Cleanup presets run silently in the background during automatic cleaning. The four Disk Cleanup commands are single-select — picking one unticks the others, since they all run the same tool.
* **Stubborn Junk Removal:** A file that will not delete on the first try is escalated through a strong, driver-free delete — clearing read-only/hidden/system attributes, extended-length `\\?\` paths, an NTFS POSIX unlink that removes files an app still holds open, and taking ownership and resetting permissions for the few that resist — so far more of what the scan finds is actually freed. The backup/restore privileges an administrator already holds are switched on, so permission-locked files are removed directly; a file still held by a running Windows service is scheduled for deletion at the next restart instead of being left as "access denied". It never kills processes or forces a restart during cleaning. Files are deleted in parallel (about 3.5x faster on an SSD in our test). A file held by an ordinary program goes to the **Kill** list; a file held only by Windows or a Windows service is scheduled for deletion at the next restart, every such file is listed after the clean, and a highlighted **Cleaning done! Restart required.** box with a **Restart now** button appears on the main window.
* **Secure Deletion (Shredding):** An opt-in **Settings** option overwrites files before deleting them so they cannot be recovered. It is **SSD-aware** (extra passes are skipped on solid-state drives, where they cannot hit the same cells and only cause wear; TRIM discards the blocks after deletion), uses a cryptographic random generator, overwrites **every data stream including alternate data streams**, never follows a symlink, forces each pass to disk, and erases the file's name and size before removal. Modes: Off, Automatic (SSD-aware), 1 / 3 (DoD 5220.22-M) / 7 passes. While it is on, the main window shows a green **Shred & Delete is on** line under the scan status (next to the Startup/Auto Clean status) with the setting and the number of passes on drive C:, and the clean log reports every location as **Shredded & Cleaned**.

### 🩺 System Health (DISM & SFC)
* **No Command Windows:** DISM and SFC run inside the app with live progress.
* **Result Panels:** Component store analysis, component store health and system file checks are shown as color-coded panels (green when healthy) with clear explanations.
* **Repair on Demand:** Tick **Run during cleaning** on a panel to clean up the component store (WinSxS), repair the Windows image (`/RestoreHealth`) or repair system files (`sfc /scannow`) as part of the clean.

### 🛡️ Security Check
* **Finds Insecure Windows Settings:** Checks more than 50 settings that weaken your PC — UAC or Windows Firewall turned off, no active antivirus, Defender disabled by policy or with whole drives excluded, SmartScreen off, **AutoPlay on for USB drives**, AutoRun, SMB 1.0, Remote Desktop without Network Level Authentication, disabled Windows Update, Guest account on, blocked Task Manager, sign-in screen backdoors, Office running all macros and more.
* **Risk Levels:** Every finding is marked HIGH, MEDIUM or LOW with an explanation of why it matters.
* **One-Click Fix & Undo:** Tick the findings and click **Fix Selected**. Previous values are backed up first, so every fix can be undone.
* Available in Utilities and as an option of the main scan, with a notification when an automatic scan finds high-risk settings.

### 🔎 One Scan, Every Check
* After the folder scan, **Firewall Rules**, **Shortcuts**, **Security Settings** and **Duplicate Files** run as steps of the scan, each with its own result panel above the scan results (green when nothing was found).
* While a step runs, the Clean button turns into **Cancel**, the progress is shown on the main screen, **Show Status** opens the tool, and **✕** skips the step.
* Nothing is changed automatically — remove rules, fix shortcuts, fix settings or review duplicates right from the panels.

### ⏰ Automatic Cleaning
* **Flexible Schedules:** Every X minutes, daily, weekly or custom days, with an allowed time range and optional **weeks of the month**.
* **Run Conditions:** Waits when the CPU is busy or the PC is in use, and never interrupts a scan or clean you started yourself.
* **Startup Cleaning:** Scan or scan and clean when Windows starts.
* **Status at a Glance:** Green status lines on the main window show when automatic or startup cleaning is on and when the next run is.
* **Notifications:** Bottom-right notifications for finished automatic runs, duplicate files and security warnings.

### 🛡️ Malware Scanner & Quarantine

> [!NOTE]
> **Test release:** the cloud malware scan is published for testing. It is released so it can be tried thoroughly and made stable, so results and behavior may still change. Please report false detections, missed threats or any problems in [Issues](https://github.com/Multron-Community/MultronWcleaner/issues).

* **Cloud Engine:** Files are checked in the cloud by the [viruskov.com](https://viruskov.com) OPEN-EDR engine over an encrypted TLS connection.
* **Quick, Full and Custom Scans:** Scan running processes, startup items and the folders you choose, or let a malware scan run automatically after the main system scan.
* **Privacy First:** By default only executables and scripts (`.exe`, `.dll`, `.ps1`, `.bat`...) are sent; personal files such as photos and documents are never uploaded while this filter is on. For each file the path of its folder is sent in two forms: a short form in which your user folder and other standard Windows folders are replaced with placeholders such as `%USERPROFILE%\Downloads` or `%APPDATA%`, and the full path as it is on your PC, including your Windows user name, so that several user profiles on one computer can be told apart. The full path is seen only by viruskov.com analysts. A license agreement explains exactly what is sent before the first cloud scan; the app loads it straight from this repository, and you can also read it here: [Cloud Malware Scan License Agreement](MALWARE_SCAN_EULA.md).
* **Hash First, Compressed Uploads:** Files the server already knows are answered by their SHA-256 without uploading them; the others are compressed before upload when that saves space.
* **Rescan and "Possibly Clean":** Any result can be scanned again without the server's saved verdict. Files almost identical to a verified clean file are marked "Possibly clean".
* **Reconnects by Itself:** If the connection drops, for example while the server restarts, the scan waits and reconnects instead of failing.
* **Right-Click Scanning:** Optional *Scan with Multron Malware Scan* entry in the File Explorer right-click menu for files, folders and drives, plus *Send To > Multron Malware Scan* for scanning several items at once. Works while the app runs in the background.
* **Quarantine Manager:** Suspicious or user-selected files can be safely isolated into a local quarantine vault with full metadata (origin path, SHA-256, size, threat label, timestamp). Quarantined files are compressed and encrypted with AES-256, so they cannot run, and are checked against their SHA-256 before they are restored. They can be exported to a ZIP protected with the password `infected`. The quarantine opens in its own window.
* **Restore / Delete:** Quarantined files can be restored to their original location at any time, deleted individually, or purged in bulk.
* **You Decide What Is Removed:** When a clean finds malicious files from the last scan, it lists them and asks before deleting anything. Automatic and startup cleans never delete them.
* **Per-File Actions:** Every result has an **Action** dropdown next to it — No action, Quarantine, Delete, Disinfect or Remove at the root — so you decide what happens to each file, like a normal antivirus. Detected files default to your chosen **Default action** (Quarantine out of the box). Use **Set all to…** to apply one action to the whole list, then the green **Apply Actions** button runs them all at once; the action controls stay disabled until the scan finishes. Removing files force-closes any program started from them, while Windows system processes are never touched.
* **Disinfect (recover the original file):** For a clean file that a virus infected *later*, right-click > **Disinfect** sends it to the cloud server, which scrapes the virus out and returns the recovered original; the infected original is kept in quarantine as a backup. The server decides per file whether it is a recoverable infection — a file that is malware itself cannot be disinfected and the app says to delete it instead. All disinfection runs on the server, never on your PC.
* **Remove at the Root:** Right-click > **Remove at the root** deletes the file and also searches for the threat's other copies (identical files elsewhere on the disk) and its autostart footprint and removes it too, so the threat cannot come back after a restart. The footprint covers the registry `Run`/`RunOnce`/`RunServices`/policy-`Run` keys (per-user and 32/64-bit), **Winlogon `Userinit`/`Shell`** (the malicious token is stripped while the real shell is kept), **`AppInit_DLLs`**, **Image File Execution Options `Debugger`** hijacks, **services** whose `ImagePath`/`ServiceDll` points at the threat (stopped and removed), the **Startup folder** shortcuts and **scheduled tasks**.
* **Bulk Cloud Scanning (HitmanPro-style):** Files are no longer checked one by one. First every target is fingerprinted locally in parallel (SHA-256 and entropy in a single read); then the fingerprints go to the server in bulk batches of up to 512 per message, paced to the server's own rate limit, and only files the cloud has never seen are uploaded. Thousands of known files are answered in seconds. Fingerprinting and checking overlap (fingerprints are sent while the rest of the disk is still hashed), and identical files are checked only once. Some connections only check fingerprints while the others upload unknown files, and unknown files are uploaded smallest first, so waiting uploads never hold up the checks. Collecting the files, fingerprinting and checking all run at the same time.
* **Fingerprint Cache:** Files whose size and modification time have not changed since the last scan are not read again — their saved SHA-256 is sent instead (the server still checks every file), which makes rescans dramatically faster. Can be turned off.
* **Parallel Connections & Compression:** Unknown files are uploaded over 1–8 parallel connections (4 by default), and optionally Brotli-compressed for slow links. These performance options sit next to the other scan settings.
* **Live Upload View (HitmanPro-style):** The scan runs in the main window instead of a separate progress window. While the disk is listed the title says *Collecting files: N found in M folders*; a status line shows how many files were found, fingerprinted, answered by the cloud and still waiting. Under the progress bar it shows Scanned, Threats (click it to open the threats found so far in a separate window), Uploaded, Elapsed and Remaining, and a scrollable **scan activity** list: each unknown file sent to the cloud gets its own row with a growing green fill, upload speed, compression ratio, "Classifying in Scan Cloud...", its verdict and a Cancel link that skips just that file, together with skipped files, files that could not be read and threats. Filter buttons with counts (**Uploads, Threats, Skipped, Errors**) and a search box narrow the list, and **Show all** resets it. The results list with the per-file actions appears when the scan finishes and has its own filter buttons and search. The Malware Scan window shows the same view in sync. An optional tray icon (off by default) shows the progress and keeps the scan running in the tray when the window is closed.
* **Restart Required, Clearly Shown:** When a locked threat can only be deleted at the next restart, the Malware Scan window shows a highlighted **Restart required** panel with those files and a **Restart now** button.
* **Local Packing Pre-Screen:** Every scanned file's **Shannon entropy** is measured locally, so packed or encrypted executables are flagged (`entropy ≥ 7.2 / 8`) even when the cloud verdict is clean — a quick heuristic that needs no upload. The value and a "likely packed" note appear in the file's details.
* **Rootkit Check (hidden processes):** A **Rootkit Check** button does a driver-free *cross-view* comparison — the normal process list versus a direct probe of every process id — to surface processes a user-mode rootkit hides, with a double pass to rule out processes that were merely starting or exiting. A kernel-mode rootkit still needs a signed driver and is out of reach of a user-mode check, which the app states plainly.
* **Stronger Deletion:** Files that resist a normal delete are removed with escalating, driver-free techniques: clearing attributes, NTFS POSIX-semantics unlink (removes a file even while it is open or locked), extended-length `\\?\` paths (for over-long names and names with trailing dots or spaces), closing the processes that lock it, taking ownership and resetting the permissions (stripping any Deny entries malware added), deletion as `SYSTEM` via Sysinternals PsExec, and finally scheduling removal at the next restart.
* **Removal History:** A quarantined or deleted file stays in the results marked "Deleted by user" / "Quarantined by user" with the time; double-click a row for the full details, threat name, SHA-256 and the steps taken.
* **Removal Progress:** Removing or quarantining threats opens a progress window with the current file, the step that is running (closing programs, taking ownership, scheduling deletion at restart...), counters for removed, at-restart and failed files, a detailed list of every step, and a Stop button.
* **Scan Logs:** Every scan writes a log with its settings, every threat and suspicious file, files that could not be checked, connection drops and a summary; removals, quarantine actions and rescans are added to it. The Scan Logs window lists the logs and shows them next to the list. Logs can be exported as text files or as one ZIP.
* **Offline Mode:** Turns off every online feature, including the cloud scan, when you do not want the app to connect to the internet.

### 🪟 Microsoft MRT Scan
* **Windows' Own Scanner, Built In:** The Malware Scan group on the main screen has a **Microsoft MRT Scan** option that scans with the Malicious Software Removal Tool (`MRT.exe`) that Windows Update installs on every PC. It is **off by default**; turn it on to scan with a **Quick Scan**, or choose a **Full Scan** that checks every fixed drive.
* **Runs Hidden, Progress on the Main Screen:** MRT runs without its own window (`MRT.exe /Q`). Its progress is shown on the main progress ring and in the malware result panel, and the Clean button turns into **Cancel** while it runs.
* **Both Scanners, One After Another:** When **Multron Malware Scan Cloud (Testing)** is also selected, MRT runs first and the cloud scan follows. The findings of both are listed together in the same malware result panel, and every row says which scanner found it: **Microsoft MRT** or **Multron Cloud**.
* **Review First, Then Remove:** By default MRT only reports what it finds (`/N`). **MRT Results** shows the findings and the full report of that scan from `mrt.log`, and **Remove with MRT** scans again and lets MRT remove the malicious software. Tick *Remove found malicious software right away* to let MRT clean during the scan instead.
* **Works Offline:** MRT does not need the internet, so it keeps working in offline mode.
* **Only When Available:** If `MRT.exe` is missing on the PC, the option is hidden and the group explains that Windows Update installs it.

### 🔒 Locked File & Process Management
* **Real-Time Lock Detection:** Surfaces file locks dynamically, displaying the active locking Process Name, Process ID (PID), and absolute file path.
* **Force Unlock & Delete:** Kills only the processes that hold the file (system processes are protected) and retries the deletion.
* **Kill for Locked Files:** After a clean, **Kill** first tries the strong delete on every locked file without closing anything; only the files still locked have their programs closed (each once, with its child processes). Windows system processes and **Windows services** (TrustedInstaller, TiWorker, VMware Tools...) are never closed — their files are deleted at the next restart instead.
* **Boot Operations Manager:** Schedules pending file removal and modification routines to execute cleanly during the next Windows reboot cycle.

### ⚙️ System & Startup Control
* **Startup Manager:** Inspects and toggles boot entries across Registry keys, Task Scheduler, and Winlogon Userinit targets.
* **Startup Sentinel:** Delivers real-time notifications whenever a newly installed application registers a boot entry.
* **Firewall Rule Cleaner:** Scans for and removes broken Windows Firewall rules that point to programs that no longer exist.
* **Shortcut Fixer:** Finds Desktop and Start Menu shortcuts to missing programs, repairs the ones whose program moved to a new version folder and moves the others to the Recycle Bin.
* **System Optimization:** Two profiles — **For old systems** pauses selected services, **For new systems** applies performance settings (power plan, Game Mode, priorities, faster menus and more). Settings are re-applied at startup, every action is listed in an Activity log, and **Undo** restores your previous values.

### 🔔 Background Scans & Notifications
* **Startup and Scheduled Scans:** The app can start with Windows in the system tray (without a UAC prompt) and scan or clean automatically.
* **Findings Reported After the Scan:** Insecure Windows settings, broken firewall rules, broken shortcuts, duplicate files and malware scan results are listed above the scan results. Nothing is changed until you confirm.
* **Actionable Notifications:** After a background scan, a notification tells you what was found, with a button that opens the details. Problems are shown with a red warning sign.
* **Your Choice:** Each kind of notification can be turned on or off, and notifications can appear in any corner of the screen, lined up or stacked on top of each other.

### 🎨 Comfort
* **Simplified or Advanced Interface:** The simplified interface (default) shows one cleaning list for all users, split into **Windows** and **Applications**, with plain item names, a checkbox on every group and the paths in the tooltips. Commands and extra scans get easy names and are gathered under **Extra Scans & Tools** at the top of the list. The advanced interface in Settings > Interface Mode looks the same but shows everything in detail: a group per user, every item on its own row with its full path, the raw commands and every database button.
* **Windows Theme:** Opens in dark mode when Windows uses dark mode and in light mode otherwise, and switches along with Windows. Choosing a theme by hand keeps your choice.
* **Transparency Effects:** Optional see-through windows with a tint color (blue, green, orange, purple, red, pink, teal or gray) and strength of your choice.
* **Quick Search:** The main screen, Duplicate File Finder and Startup Manager share the same search box: type to filter, press Esc or ✕ to clear.
* **Consistent Look:** Themed message windows with a colored icon that fits each message (also in dark mode), matching scroll bars in every window, and windows that always fit on the screen.
* **Automatic Saving:** Settings are saved as soon as you change them; there is no Save button. The Settings window can be resized, maximized and reset to defaults.
* **Updates Built In:** The app and the cleaning database can check for updates in the background, at an interval you choose, or on demand from Settings > App Updates. The app updates itself without a separate updater: it swaps its own files, restarts and puts the old files back if anything fails. Your settings are never overwritten, and update packages that would write outside their own folder are refused.
* **One Copy at a Time:** Starting the app again, or scanning from the right-click menu, uses the copy that is already running instead of opening a second one.
* **12 Languages:** The language can be changed at any time in Settings.

### 📊 Memory & Performance Tracking
* **RAM Optimizer:** Manual or interval-based memory working set reductions.
* **Real-Time Resource Monitor:** Continuous visual tracking for active CPU and memory utilization.

### 🔍 Storage Utilities
* **Duplicate File Finder:** Byte-by-byte verified, A **side-by-side view** shows each copy next to its original, 40 quick-select file types and presets for images, videos, music, documents, archives and installers, and a safe delete that always keeps one copy.
* **Large File Finder:** Scans drive structures to isolate oversized media, archives, installers, disk images and virtual machines, backups, dumps and logs.
* **Pre-Deletion Audit:** Comprehensive summary table displaying each file's size, creation date, modification date, and age in days prior to execution.

---

## System Requirements

* **OS:** Windows 10 / Windows 11 (64-bit)
* **Privileges:** Administrator permissions (required for DISM operations, boot-time actions, and Registry access). Because of this, starting the app, including from the right-click menu, shows a UAC prompt; starting with Windows does not.
* **Runtime:** [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

---

## Installation

1. Navigate to the [Releases](https://github.com/Multron-Community/MultronWcleaner/releases) section.
2. Download one of these:
   * `mwc_setup.exe` installs the app for your user in `%LOCALAPPDATA%\Programs\Multron Win Cleaner` (no administrator rights needed for setup) and updates an existing installation in place.
   * `mwc.zip` is the portable version: extract it anywhere and run `MultronWinCleaner.exe` from the `mwc` folder.
3. Start the app and confirm the UAC prompt. Later updates can be installed from Settings > App Updates.

---

## Automatic Updates

Multron Win Cleaner updates itself from the GitHub releases of this repository (Settings > App Updates). Since 1.26.4 every update is verified before it is installed:

* **Signed releases.** Each release has an `update.json` with the version and the SHA-256 of every file, signed with the Multron Win Cleaner release key (`update.json.sig`, ECDSA P-256). The public key is built into the app, so a file that was changed on GitHub without the private key is refused.
* **Checked file.** The downloaded `mwc.zip` must match its SHA-256 in the signed `update.json` before it is unpacked.
* **No going back.** The signed version must match the release and be newer than the installed version, so an old signed release cannot be used to install an older version.
* **Official source only.** The update, its manifest and its signature must come from `https://github.com`.
* **Signed database.** Since 1.26.5 the cleaning database is verified the same way, with its own key: each [database release](https://github.com/Multron-Community/MultronWcleaner-Database/releases) has a signed `update.json`, and a `database.txt` that does not match it is refused while the current database stays in use.
* **Protected key.** The release workflow builds the app without any secrets. A separate job in the `release-signing` environment signs exactly the files that build produced, adds a GitHub build attestation and publishes the release.

Check a download yourself with `node scripts/verify-update.mjs update.json update.json.sig mwc.zip` from this repository, or `gh attestation verify mwc.zip --repo Multron-Community/MultronWcleaner`. Versions before 1.26.4 still update normally; they install 1.26.4 without the signature check, and every update after that is verified.

**For maintainers:** the app signing key was created with `node scripts/update-keys.mjs`; its public half is `PublicKey` in `MultronWinCleaner/Processes/UpdateKey.cs` and the private half is the `UPDATE_SIGNING_KEY` secret of the `release-signing` environment. The database key lives in the database repository (`node scripts/update-keys.js`, `update-key.pub`, its own `release-signing` environment); its public half is `DatabasePublicKey` in the same file. Keep an offline backup of it: installed copies only accept updates signed with this key.

---

## Cloud Malware Scan Protocol (v3, hash first)

How Multron Win Cleaner talks to the viruskov.com OPEN-EDR scan server.

- Client: `MultronWinCleaner/Processes/CloudScanClient.cs`
- Server: `multron_server.exe` (Rust, source in `HydraDragonAntivirus/OpenEDR/multron_server_rs`), run from the `OpenMalwareScannerPortable` folder next to the engine rules.

### Transport

- WebSocket (RFC 6455). In production the client connects to `wss://api.viruskov.com/ws` through a Cloudflare Tunnel: Cloudflare terminates TLS and the tunnel forwards plain `ws://` to the server (default port `5306`, path `/ws`).
- The URL is compiled into the client (`CloudScanClient.ServerUrl`). Unencrypted `ws://` is accepted only for a server on the same PC (localhost).
- `CloudScanClient.ServerCertSha256` is only for a direct `wss://` connection to a server with a self-signed certificate; leave it empty behind Cloudflare.
- One connection is reused for all files of a scan session. Keep-alive frames are sent every 20 s.

### Flow

1. The client hashes every file (streamed, the file is not loaded into memory).
2. Hashes go to the server in `check` batches (up to `checkBatch` items per message).
3. The server answers each hash from what it already knows, without the file:
   - its **verdict cache**: every file any client ever uploaded (shared by all clients, kept on disk in `multron_cache.jsonl`; clean/malicious verdicts 14 days, unknown 24 hours);
   - **hash signatures** (`malicious_sha256.txt`, EICAR); a malicious hit wins over everything;
   - the engine's **SHA-256 whitelist** (`xorfilter_rules/benign_sha256.xf`);
   - a file **another client is uploading right now**; the answer is sent when that scan ends.
4. Only files answered with `need_upload` are read and uploaded with `scan`.

### Messages

Every control message is one WebSocket **text** message containing JSON (max 1 MiB). File contents are sent as one or more WebSocket **binary** messages.

**Handshake**

```
C → S  {"type":"hello","version":3,"client":"MultronWinCleaner","token":"<optional>"}
S → C  {"type":"hello_ok","version":3,"engine":"OpenEDR static","pipeline":4,"checkBatch":512,"maxMB":100,"encodings":["br"]}
       or {"type":"error","message":"..."}   (server closes)
```

The server accepts version 2 clients (no `check`, every file goes through `scan`).

**Hash check**

```
C → S  {"type":"check","items":[{"id":1,"name":"a.exe","size":123456,"sha256":"<64 hex>","folder":"%USERPROFILE%\\Downloads","path":"C:\\Users\\Me\\Downloads"}, ...]}
S → C  {"type":"result","id":1, ...}         known file: done, nothing is uploaded
S → C  {"type":"need_upload","id":2}         unknown: upload it with scan
S → C  {"type":"error","id":3,"message":"..."}
```

`folder` is the file's folder with the user profile and other well-known folders replaced by placeholders such as `%USERPROFILE%`; `path` is the same folder in full, exactly as it is on the computer, including the Windows user name (agreement version 3), so several user profiles stay apart on the server. The full path is seen only by viruskov.com analysts. `"rescan":true` asks for a fresh scan that skips the verdict cache.

**Upload**

```
C → S  {"type":"scan","id":2,"name":"b.dll","size":5000,"sha256":"<64 hex>","folder":"...","path":"..."}
S → C  {"type":"send_file","id":2}          (or a result right away if it became known meanwhile)
C → S  exactly <size> bytes, split into binary messages (256 KiB chunks)
S → C  {"type":"result","id":2, ...}        when the engine is done
```

- When the server lists `"br"` in `encodings`, the client may Brotli-compress an upload that gets at least 10% smaller and adds `"encoding":"br","csize":<compressed size>` to `scan`.
- After `send_file`, the next binary bytes belong to that id; the client finishes the upload before sending anything else. Checks wait while an upload is being sent.
- The client keeps at most `pipeline` uploads unanswered; the server holds the connection's reader until a slot is free, so more would block hash checks too.
- The server verifies the SHA-256 of the received bytes.
- An error for one file comes as `{"type":"error","id":N,...}` and the connection stays usable. An `error` without `id` ends the connection.

**Result message**

```json
{
  "type": "result",
  "id": 1,
  "verdict": "clean | possible_clean | malicious | suspicious | unknown",
  "threat": "Win.Trojan.Example",
  "detail": "Win.Trojan.Example (ClamAV)",
  "score": 1.0,
  "sha256": "...",
  "scan_ms": 810,
  "source": "scan | cache | whitelist | hash | shared"
}
```

### Server side

- Engine work runs on its own thread pool (`--workers`, default CPU cores − 1). Clients take turns (round-robin), so one client with 100k files does not hold back the others.
- A panic in the engine fails only that file: it is reported `suspicious` (`Unscannable.EngineCrash`) and remembered, so it is never scanned again.
- Files the engine cannot classify (`unknown`) and detected threats are kept in the work folder as `<SHA256>_<name>` for review, each within its own disk quota; clean files are kept only when that is turned on.
- Limits: `--max-conns` (2048), `--max-per-ip` (8, real IP from `CF-Connecting-IP` behind the tunnel), `--token`, `--max-mb` (100, hard ceiling: a larger value is clamped to 100), `--max-inflight-mb` (768).
- Flood protection: one WebSocket message is at most 1 MiB (uploads come in 256 KiB chunks); per IP 120 new connections/min and 2048 MB uploaded/hour; per connection 100 messages/s and 3000 hash checks/s. Breaking a limit (or a bad token, invalid JSON, wrong hash, oversized upload) is a strike; 15 strikes block the IP for 15 minutes (HTTP 429 before the WebSocket upgrade).

### Timeouts and reconnecting

| Side | What | Limit |
|---|---|---|
| Client | connect + handshake | 15 s |
| Client | waiting for a check answer / `send_file` / `result` | 10 min |
| Server | idle connection | 30 min |
| Server | gap between two upload chunks | 2 min |

If the connection drops during a scan, the client reconnects one attempt at a time for all scan workers, waiting 2, 4, 8, 15 and then 30 s between failed attempts. A file is tried at most 3 times. If the server cannot be reached for 4 minutes, or it rejects the connection (bad token, old protocol, blocked IP), the scan stops with an error. A rescan can be stopped at any time.

A connection that gets no reply for 120 s while checks are waiting is treated as dropped and reconnected. The client keeps at most 256 checks waiting per connection, sends check messages of at most 50 hashes at most every 25 ms per connection and paces the number of hashes to the server's `checksPerSec` (85 % of it), or to 500 per second when the server does not send it; when the server answers with an error asking to slow down, the client halves its check rate for that connection's next sessions. If the server refuses the first connection with HTTP 429 (too many connections from the address), the client retries after 3, 6 and 12 s and then reports it.

### What the client sends

- Quick and Full scans send only executables and scripts. Custom scans send other files only if the user turns off "Only executables & scripts".
- Files larger than the user's limit (default 50 MB) are skipped. The server refuses files above its own `--max-mb` limit.

---

## Community Database Contributions

The cleaning rules are kept outside the main codebase to allow rapid additions without needing a new version release. You can inspect or extend the rule definitions directly at the [MultronWcleaner-Database](https://github.com/Multron-Community/MultronWcleaner-Database) repository.

Pull Requests adding support for new applications or custom directory paths are always reviewed and merged there.

---

## Support the Project

Multron Win Cleaner is free, open-source and has no ads, subscriptions or locked features. If it helps you and you would like to support its development, you can do so on Patreon:

<a href="https://www.patreon.com/cw/multron"><img src="https://img.shields.io/badge/Support%20on-Patreon-F96854?style=for-the-badge&logo=patreon&logoColor=white" alt="Support on Patreon" /></a>

Every bit of support is appreciated, but the app stays completely free either way.

---

## License

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for complete details.

### Third-party components

Multron Win Cleaner bundles **Sysinternals PsExec** (`PsExec64.exe`), used by the Force Delete / Force Move feature to remove locked or protected files with SYSTEM privileges. PsExec is a Microsoft Sysinternals tool and is not covered by the MIT license above; it is governed by the Sysinternals Software License Terms. See [PSEXEC-EULA.md](PSEXEC-EULA.md).
