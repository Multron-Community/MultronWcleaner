## Multron Win Cleaner 1.27 Update

> **Test release:** the cloud malware scan and its new disinfection are published for testing, so results and behavior may still change. Please report problems in [Issues](https://github.com/Multron-Community/MultronWcleaner/issues).

### New: Disinfect — recover a file a virus infected later
A file that was clean and got infected by a virus *afterwards* can now be repaired instead of only deleted. Right-click a detected file in the Malware Scan results and choose **Disinfect (recover the original file)**:
- The file is sent to the cloud server, which **scrapes the virus out and returns the recovered original**. All of this runs on the server, never on your PC.
- The **infected original is kept in quarantine** as a backup before the recovered file is written back in its place.
- The server decides per file whether it is a recoverable infection. A file that **is malware itself** cannot be disinfected; the app says so and you delete it instead. You can disinfect several selected files at once, so other files hit by the same virus are cleaned in one step.

### New: Much faster cloud scans  
The malware scan no longer checks files one by one:
1. **Fingerprinting:** every file is hashed locally in parallel, with its entropy measured in the same read (no second read).
2. **Bulk check:** fingerprints go to the server in batches of up to **512 per message**, paced to the server's own rate limit, so thousands of known files are answered in seconds.
3. **Upload only the unknown:** only files the cloud has never seen are uploaded, over **1–8 parallel connections** (4 by default).

Fingerprinting and checking now **run at the same time**: the first fingerprints go to the server while the rest of the disk is still being hashed. **Identical files** (the same content in several places) are checked only once, and the result is applied to every copy.

New options in the scan settings: **Connections**, **Remember fingerprints of unchanged files** (rescans skip re-reading files whose size and modification time did not change — the server still checks every file) and **Compress uploads** (Brotli, for slow links). The server now tells the client its check rate so the client never exceeds it; with a server that does not say it yet, the client starts at a safe 500 checks per second, sends small check messages (at most 50 hashes, at most 40 messages per second per connection), keeps at most 256 checks waiting per connection so replies keep flowing, and halves its rate on its own when the server asks it to slow down.

### New: Live upload view in the main window  
The malware scan no longer opens a second progress window; it runs in the **main window**. Under the progress bar ("Classifying (45%)...") and the current file it shows **Scanned, Threats, Uploaded, Elapsed and Remaining** (estimated time left), then a scrollable **scan activity** list that is the centre of the scan: every **unknown file sent to the cloud** (name and path, a green fill that grows with the upload, the **upload speed** and the **compression** achieved, then "Classifying in Scan Cloud..." and finally its verdict), plus skipped files (for example over the size limit), files that could not be read and threats as they are found. Clicking the **Threats** count opens a separate window with the threats found so far; the results list with the per-file **actions** appears when the scan finishes. Files still in the queue are counted above the list. A **Cancel** link skips that one file without stopping the scan (a file already being sent finishes its transfer first, so the connection is never broken). The same view is shown in the Malware Scan window, in sync.

- **Filters and search:** the scan activity list has filter buttons with counts — **Uploads, Threats, Skipped, Errors** — so you can show only, for example, the unknown files sent to the cloud, plus a search box for a file name or path; **Show all** puts it back. Your choice is remembered and is the same in the main window and the Malware Scan window. The results list after the scan has the same kind of buttons (**Threats, Clean, Uploads, Skipped, Errors**, replacing "Show threats only") and its own search box.
- **Scanning starts at once:** collecting the files, fingerprinting them and checking them with the server now all run at the same time, instead of waiting for the whole disk to be listed first.
- **Every stage is visible:** while the disk is being listed the title says **Collecting files: N found in M folders**, and a status line under the progress bar shows how many files were found, fingerprinted, answered by the cloud and still waiting for it, and how many connections are open (also written to the scan log every 15 seconds).
- **Restart required after threat removal:** when a threat file is locked and can only be deleted at the next restart, the Malware Scan window shows a prominent **Restart required** panel with those files and a **Restart now** button (the same as after cleaning).
- **Small unknown files first:** unknown files are uploaded smallest first, so many more verdicts arrive in the same time and large files are sent at the end.
- **Tray icon (optional):** a new **Show a tray icon while scanning** option (off by default) shows an animated tray icon with the scan progress and a notification when it finishes; with it on, closing the Malware Scan window during a scan keeps the scan running in the tray.
- **Much faster scans with unknown files:** the connections now have separate jobs — some only check fingerprints (half of them from 4 connections up, otherwise one), the others only upload unknown files. Before, a single file waiting for the server to accept its upload held up the checks of thousands of known files on the same connection; now known files keep being answered while uploads wait. The client also stays within the server's upload window, and up to **8 connections** can be used.
- **A scan no longer hangs when the server goes quiet:** if a connection gets no reply for 2 minutes while checks are waiting, the client drops it and reconnects on its own ("The cloud server stopped answering" in the scan log) instead of waiting forever. Every 15 seconds the scan log also lists, for each connection, how many checks were sent and in how many messages, how many replies came back and when the last one arrived, the check rate in use and whether the server asked to slow down — so a slow scan can be explained.
- **Clear message when the server is limiting you:** if the server refuses new connections because too many scans were started in a short time, the scan retries a few times (3, 6 and 12 seconds apart) and then says so in plain words instead of showing a raw "status code 429" error.
- **Fewer "could not be read" errors:** files that Windows protects even from administrators (for example Defender's update files in `C:\Windows\SystemTemp`) are now read with backup privileges.

### New: Per-file actions
Every row in the scan results now has an **Action** dropdown next to the file — **No action, Quarantine, Delete, Disinfect** or **Remove at the root** — so you decide what happens to each file individually. Detected files default to **Quarantine**; change any of them, or use the header checkbox to select all, then press **Apply Actions** to carry out every chosen action in one go.

### New: Remove at the root
Right-click a threat and choose **Remove at the root (file + copies + startup)** to delete it *and* everything it left behind: the threat's **other copies** (identical files elsewhere on the disk) and its full **autostart footprint** — registry `Run`/`RunOnce`/`RunServices`/policy-`Run` keys, **Winlogon `Userinit`/`Shell`** (the malicious token is stripped and the real shell kept), **`AppInit_DLLs`**, **Image File Execution Options `Debugger`** hijacks, **services** pointing at the threat (stopped and removed), Startup-folder shortcuts and scheduled tasks — so it cannot come back after a restart.

### New: Local packing pre-screen and Rootkit Check
- **Packing pre-screen:** every scanned file's **Shannon entropy** is now measured locally, so packed or encrypted executables are flagged (entropy ≥ 7.2/8) even when the cloud verdict is clean — no upload needed. The value shows in the file's details.
- **Rootkit Check:** a new button does a driver-free *cross-view* check for **hidden processes** (the normal process list versus a direct probe of every process id). It is a limited user-mode check — a kernel-mode rootkit still needs a signed driver — and the app says so.

### Stronger deletion of stubborn files
Files that resist a normal delete are now removed with stronger, driver-free techniques: **NTFS POSIX-semantics unlink** (removes a file even while it is open or locked), **extended-length `\\?\` paths** (for over-long names and names with trailing dots or spaces), and a tougher permission reset that strips the **Deny** entries malware adds, in addition to the existing ownership, PsExec-as-`SYSTEM` and delete-at-restart steps.

### New: Secure deletion (shredding) for privacy
A new **Secure Deletion (Shredding)** option in Settings makes the cleaner overwrite files before deleting them, so what it removes cannot be recovered. It is **SSD-aware**: on a solid-state drive, extra overwrite passes cannot target the same memory cells (wear levelling) and only wear the drive, so a single clearing pass is used and the drive's TRIM discards the blocks; spinning disks get a multi-pass wipe. Modes: Off, **Automatic (SSD-aware, recommended)**, 1 pass, 3 passes (DoD 5220.22-M) or 7 passes. The wipe uses a cryptographic random generator, overwrites **every data stream including alternate data streams**, never follows a symlink to its target, forces each pass to disk, and finally erases the file's name and size before removal. (As with any file-level shredder, data in Volume Shadow Copies/backups, or remapped by SSD wear levelling, is outside its reach — the setting explains this.)

### Per-file actions and a cleaner results toolbar
The Malware Scan results now have an **Action** dropdown next to every file — No action, Quarantine, Delete, Disinfect or Remove at the root — plus **Set all to…** to choose one action for the whole list and a green **Apply Actions** button that runs them. The old "Remove Selected" / "Quarantine Selected" buttons are gone (the per-file actions replace them), the action controls stay disabled until a scan finishes, and a new **Default action** setting pre-selects what detected files should do.

### Changes
- **System cleaning removes much more of what it finds.** When a junk file would not delete on the first try, the cleaner now escalates to the strong, driver-free delete (clears read-only/hidden/system attributes, uses extended-length paths, an NTFS POSIX unlink that removes files an app still holds open, and — for the few that resist even that — takes ownership and resets permissions) instead of giving up and marking it locked. This closes most of the old gap where, for example, 70 GB was found but only part of it was actually freed. It still never kills processes or forces a restart during cleaning.
- **Faster cleaning.** Files are now deleted in parallel (about 3.5x faster on an SSD in our test with 20,000 files; 2 at a time when shredding), the selected files of each location are deleted directly instead of listing the whole folder tree again, deep log files no longer update the screen after every single file, and failures are written to `exceptions.txt` once at the end instead of rewriting the file for every file that could not be deleted.
- **Stronger "Kill" for locked files.** Before closing any program, every locked file is first tried with the strong delete (it removes many files that are only open, without closing anything). Only the files that are still locked have their programs closed — each program once, together with its child processes, and never Windows system processes — and are then deleted again. A file that still cannot be deleted is marked to be deleted at the next restart instead of being left behind. **Windows services are never closed** (for example TrustedInstaller, TiWorker or VMware Tools); their files are deleted at the next restart instead.
- **More locked files reach the Kill list.** When a file cannot be deleted, the cleaner now asks Windows which program holds it: if it is an ordinary program (a browser, Discord, an office app...), the file goes to the Kill list — also when shredding — instead of being quietly marked for the next restart; only files held by Windows itself or a Windows service are left to the restart.
- **"Restart required" is impossible to miss.** When cleaning (or Kill) leaves files to be deleted at the next restart, the main window shows a highlighted **Cleaning done! Restart required.** box with the number of files and a **Restart now** button (it asks first), and the clean log ends with a highlighted banner. Each file is counted once, even when several cleaning locations point to it.
- **See what is deleted at the next restart.** After cleaning, the files marked to be deleted at the next restart are listed one by one under the summary, and so are the files that could not be deleted at all, with the Windows error for each. Marking a file for deletion at the next restart is also more reliable now (it falls back to writing the restart list directly when Windows' own call refuses).
- **Far fewer "access denied" files.** The cleaner now enables the backup/restore privileges an administrator already has (Windows ships them switched off), so files whose permissions block deletion are removed directly, without spawning takeown/icacls per file. A file that is still held by a running Windows service is handed to Windows to delete at the **next restart** and reported as such instead of "access denied".
- **Shredding is visible while it runs.** With Secure Deletion on, the main window shows a green **Shred & Delete is on** line under the scan status, next to the Startup/Auto Clean status, with the chosen setting and how many passes it makes on drive C: (SSD/HDD), and the clean log says **Shredding & Cleaning / Shredded & Cleaned** for every location and **files shredded and deleted** in the summary.
- **The Windows Disk Cleanup (cleanmgr) commands are now single-select.** Picking one of the Disk Cleanup options unchecks the others, since they all run the same tool.
- **Microsoft MRT Scan is now off by default.** Turn it on in the Malware Scan group when you want it; your choice is remembered.
- The scan now tells the server whether it **finished** or was **cancelled by the user**, so the server dashboard shows which it was instead of a bare dropped connection.

### Updating
- **From 1.26.7 or earlier:** the app updates itself to 1.27 as usual (Settings > App Updates).
- **New installs:** download `mwc_setup.exe` (installer) or `mwc.zip` (portable) below.

---

## Multron Win Cleaner 1.26.6 Update

> [!WARNING]
> **Antivirus false positives:** some antivirus programs may flag Multron Win Cleaner or one of its DLL files as suspicious when you download or run it. In particular, **Bitdefender** and engines that use it (for example Emsisoft, eScan, GData, Arcabit, VIPRE) may detect `MultronWinCleaner.dll` **heuristically as ransomware**, with a name such as `Gen:Heur.Ransom.Imps.3`. This is a false positive: a heuristic detection is based on behavior, not on a known malware signature, and it is triggered by what a system cleaner has to do (closing programs, deleting files, changing Windows settings). Multron Win Cleaner does not encrypt files, does not ask for a ransom and does not send your data anywhere except the optional cloud malware scan. The file has been reported to Bitdefender as a false positive again. The full source code is in this repository, and every release is built automatically by GitHub Actions. If your antivirus blocks the app, you can report the file to its vendor as a false positive or add an exception for the app folder.
>
> * Bitdefender false positive form: <https://www.bitdefender.com/consumer/support/answer/29358/>
> * Microsoft Defender file submission: <https://www.microsoft.com/en-us/wdsi/filesubmission>

> **Test release:** the cloud malware scan is published for testing. It is released so it can be tried thoroughly and made stable, so results and behavior may still change. Wrong scan results can now be reported straight from the app (see below); please report any other problems in [Issues](https://github.com/Multron-Community/MultronWcleaner/issues).

### New: Report wrong scan results
Scan results that you believe are wrong can now be sent to the viruskov.com analysts from the Malware Scan results. Right-click one or more files:
- **Report False Positive:** for a file detected as malicious or suspicious that you believe is clean.
- **Report False Negative:** for a file the scan found clean that you believe is malicious (a missed threat).

You can add an optional note (up to 500 characters), for example where the file comes from or why you think the result is wrong. The report sends the file's SHA-256, its name and your note; the note is only shown to the analysts. If the server does not have the file yet, the app scans it again so it is uploaded for the analysts, with the same rules as a normal cloud scan. If the result changes on that new scan, the file is not reported and the app shows the new result.

The analysts check each reported file, and their verdict replaces the engine verdict for everyone: the next scan of the file shows it. If an analyst has already reviewed a file, the app shows that verdict instead of sending a new report. Every report is also written to the malware scan log.

### Changes
- The Malware Scan page no longer shows the server hosting information.

### Updating
- **From 1.26.5 or earlier:** the app updates itself to 1.26.6 as usual (Settings > App Updates). App updates are checked against their signature since 1.26.4.
- **New installs:** download `mwc_setup.exe` (installer) or `mwc.zip` (portable) below.

For signed database updates see the [v1.26.5 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.5), for signed app updates the [v1.26.4 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.4), and for the full list of what is new in 1.26 the [v1.26.3 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.3), the [v1.26.2 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.2), the [v1.26.1 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.1) and the [v1.26 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26).
