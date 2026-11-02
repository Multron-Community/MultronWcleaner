## Multron Win Cleaner 1.28 Update

### New: Close open browsers and apps before cleaning
Browsers and apps such as Discord keep their cache files locked while they run, so those files could not be cleaned. Before cleaning, Multron Win Cleaner now checks whether a browser or app whose files the scan found is open and asks: **"Google Chrome is open. Close Google Chrome to clean all of its files found by the scan?"** with **Yes, close**, **No, skip** and **Don't ask again**.
- **Yes, close** closes it normally first, so a browser brings its tabs back the next time you start it, and closes it for good after 10 seconds if it does not respond.
- **No, skip** leaves its files alone; they are listed as *Skipped, the program is open*.
- The right program is closed even when several browsers use the same program name (Google Chrome, Chromium and other Chromium browsers all run as `chrome.exe`): the app checks the browser's own profile lock and the folder the program runs from.
- The choice can be changed in **Settings > Open Browsers and Apps**: ask every time, close without asking, or skip.
- Background and startup cleanings never close programs.
- This uses the browser and app tags of the cleaning database v1.15 (65 browsers, 35 apps such as Discord, Slack, Teams, Spotify, Telegram, WhatsApp, Zoom and Visual Studio Code).

### Confirmation before risky items are cleaned
Some cleaning locations delete files Windows needs or your own files: the Downloads, Screenshots and Captures folders, the driver store, System Restore data, scheduled tasks, the page, swap and hibernation files and a few more. They are never selected by default, but Select All selects them too.
- When any of them is selected, cleaning now first lists them with what each one deletes and asks **Clean them too?** If you choose No, only these items are skipped (*Skipped, not confirmed*); everything else is cleaned as usual.
- Automatic and startup cleanings never clean them, since nobody is there to confirm.
- Every item with a warning now has a **⚠ Warning** button next to it in the list; press it to read what the item deletes. Besides the Windows items above, this covers everything that deletes personal data: saved passwords, cookies, history, open tabs, form and payment data, website data and program settings.
- The same confirmation is asked when you select any of these and clean.
- Every item that is not selected by default now has a blue **ⓘ Info** button that says why, for example *Not selected by default: logs help to find the cause of problems*. Info notes are only shown; they never ask for confirmation.
- **Settings > Warnings and Notes** has two options, both on by default: show the warning and info buttons, and ask before cleaning items with a warning. Background and startup cleanings never clean items with a warning either way.

### Better shortcut fixer
- **More places:** taskbar pins and the Send To menu are checked too, besides the Desktop and the Start menu.
- **Smarter repair:** a shortcut to a program that moved to another folder is now pointed to its new place when Windows lists the program as installed (App Paths and installed programs), and only when the new place belongs to the same program.
- **Backup:** a repaired shortcut is saved first to `ShortcutBackup` in the app folder; removed ones still go to the Recycle Bin.
- **No empty folders:** Start menu folders left empty after removing broken shortcuts are removed too.

### Duplicate File Finder finds more
- **Smaller files too:** the default minimum size is now 100 KB instead of 1 MB, so most documents and many photos are compared as well.
- **The file types you see are the ones scanned:** the ticked file types were not applied when the window opened, so the first scan looked at every file type. The filter now matches the ticked boxes, and more types are ticked by default: older Office and OpenDocument files, RTF, camera RAW photos (`.cr2`, `.nef`, `.arw`, `.dng`), TIFF, more audio and video formats, e-books, installers (`.exe`, `.msi`, `.apk`) and `.tar`/`.gz` archives.
- **More places in the main scan:** besides Desktop, Documents, Downloads, Pictures, Videos and Music, the duplicate step now also checks the Public folders, your OneDrive folders and your other fixed drives. Downloads is found where Windows keeps it, also when you moved it to another drive.
- **Less noise:** program and system folders are skipped (`AppData`, Program Files, ProgramData, game libraries such as Steam and Epic, `node_modules`, `.git`), so their own files are not offered as duplicates. A folder you add yourself is always scanned.

### Apply both system optimizations at once
System Optimization has a third choice, **All: old and new system optimizations**. It pauses the background services and applies the performance settings together; only the one that is not on yet is applied, and Undo restores both. **Configure** then lets you pick which list to change. Undoing one optimization no longer clears the activity log of the other one that stays on.

### Visual effects for best performance
The performance boost for new systems has a new setting, on by default: **Visual effects for best performance**. Like *Adjust for best performance* in Windows, it turns off the animations, fades and shadows (window animations, menu and tooltip fades, taskbar animations, shadows under windows and the mouse pointer, the translucent selection rectangle and icon label shadows), but it keeps what makes Windows comfortable to use: window contents while dragging, smooth screen fonts, thumbnails instead of icons, Peek, sliding combo boxes and smooth scrolling. It takes effect at once, without signing out, and Undo brings back exactly the effects you had before.

### New network optimizations
Two new items under System Optimizations:
- **Restore TCP auto-tuning:** when TCP receive window auto-tuning is turned off, downloads get slower. This sets it back to normal only when it was changed, checks it again at startup, and Undo brings your previous setting back.
- **Repair the network connection** (off by default): resets Winsock and TCP/IP for when the internet does not work at all. It needs a restart and cannot be undone.

### Faster Malware Scan
- **Up to twice as many fingerprint checks per second.** Half of the connections only upload unknown files, so on a rescan, when almost nothing is unknown, they sat idle while the other half checked every fingerprint. They now help with the checks whenever nothing is waiting to be uploaded, and go back to uploading as soon as an unknown file is found, so uploads still never hold up the checks. The server limits checks per connection, so this doubles the check speed with 4 connections; up to 8 connections can be set in the scan settings.
- **Quick Scan collects its files sooner.** Running programs, the modules they loaded, startup items, services and scheduled tasks, and the system folders are now collected at the same time instead of one after another. The loaded modules are read straight from Windows and from all programs in parallel: in our test 1,591 modules in 0.18 s instead of 1.3 s, the same list as before.
- **Fingerprinting is up to 7 times faster with many small files.** Every file used to get a new 1 MB read buffer and was buffered twice. Buffers are now reused and read only once, so in our test 20,000 small files were fingerprinted in 0.16 s instead of 1.16 s, with about 12 MB of memory allocated instead of 20 GB. Large files go at the speed of the disk as before. The fingerprints are exactly the same.
- **Remember clean results (smart caching):** a new setting, **1 month** by default (Off, 6 hours, 1 day, 3 days, 1 week or 1 month). A file that was clean at a recent scan and has not changed is not asked again during this time, so rescans finish much sooner and the server gets fewer checks. Results are remembered by the file's content (SHA-256), so every copy and a renamed file use them too. Only clean results are remembered; files that were new to the cloud are trusted for a quarter of the time, packed or encrypted files are always checked, and a file you report as a missed threat is forgotten at once. Choose **Off** to have every file checked by the server on every scan.
- **Disk type:** on a hard disk (HDD) only 2 files are read at a time, since reading many at once makes the disk head jump back and forth. **Automatic** asks Windows which drives are hard disks; SSDs keep reading many files in parallel.
- **Scan priority:** **Low** keeps the PC responsive by lowering the CPU and disk priority of the scan, **High** gives it more CPU time. Normal is the default.
- **Clear scan caches** forgets the saved fingerprints and remembered results, so the next scan reads and checks every file again.
- **Largest file to check:** 256 MB, 512 MB, 1 GB or 2 GB (the default, as before). Larger files are listed as skipped without being read; big game, video and disk image files are rarely malware, so a lower limit makes Full Scans faster.
- **Find programs renamed to another extension** can now be turned off. It opens every file with an unknown extension to look for a program header, which is the slowest part of collecting files in a Full Scan. When it is on, the answer is now remembered until the file changes, so a file is opened for this only once.
- **Unchanged files cost almost nothing.** The size and date of a file are taken from the folder listing instead of being asked from Windows again, so a file whose fingerprint is saved is not touched at all.
- **The results list no longer slows the scan down.** Every new result made the window count the whole list again, so with 100,000 files the window spent most of its time recounting. It is now updated once after each batch.
- **The scan log is written in batches** twice a second instead of opening and closing the file under a lock for every line, so reading threads no longer wait for each other while writing skipped files and errors.
- **Faster fingerprint cache:** it is saved in a compact binary file that loads several times faster, and entries for files not seen for 60 days are dropped. Before, once it held a million files no new file was remembered at all.

### Developer mode: check a file on VirusTotal
With the Malware Scan developer mode on, right-click files in the results and choose **Check on VirusTotal** (up to 4 at a time). Nothing is sent during a scan; only the files you pick are checked.
- With your own **VirusTotal API key** (developer settings, saved encrypted for your Windows user with DPAPI) the app shows how many engines detect the file and their names, and can open the full report.
- If VirusTotal does not know the file yet, the app asks before uploading it (up to 32 MB) and warns that uploaded files are shared with VirusTotal's partners.
- Without a key the file's page opens on the VirusTotal website by its SHA-256.

### New: Additional databases
The official database is no longer the only one. Open **Database ▾** in the main window to use more databases next to it:
- **From a file:** **Add from file...** lets you choose one or more `.txt` databases anywhere on your PC. They are checked, copied to the `Databases` folder next to the app and turned on. Databases you put in that folder yourself are listed too, and each one can be turned on or off with its check box.
- **From an internet address:** **Add from internet address...** downloads a database from an `https://` address (up to 5 MB). It is downloaded again with **Update Database** and with the automatic database check.
- The groups of an additional database are shown with its name, for example *Game Caches (mydb)*, and the official database is still updated as before without touching yours. Remove a database with the bin button next to it.
- **Safety:** a database decides which files are deleted, so items that only come from additional databases are never cleaned by automatic or startup cleaning, and additional databases cannot run programs (cleanmgr, Dism.exe, sfc and the deep log scan lines are ignored in them).

### A more consistent look
- **The same title bar buttons in every window:** minimize, maximize and close now use the Windows 11 icons everywhere, and the close button turns red when you point at it. Before, every window had its own mix of `-`, `—`, `─` and `✕`, and in Memory Cleaner and Large File Finder the close button even showed a dash.
- **A tidier main window toolbar:** the seven buttons that took up three rows are now one row: **Select All**, **Unselect All**, a **Selections** menu (Save, Write to Database, Reset) and a **Database** menu (Reload, Update), next to the search box.
- **Modern buttons, check boxes and option buttons** in both themes: rounded buttons that light up when you point at them, and rounded check boxes and option buttons in place of the old white Windows ones (for example in Memory Cleaner).
- **Cleaner tables:** column headers in Startup Manager, Large File Finder and other lists are plain text over a thin line instead of a grey bar, and the row under the mouse is highlighted.

### Tidier tool windows
- **Settings in their own window:** the Duplicate File Finder and Malware Scan windows no longer unfold their settings above the results. A new **Settings** button in the title bar opens them in a separate window, so the main window only shows the scan and its results. The settings window stays small enough for low screen resolutions and scrolls when its content does not fit.
- **Modern dropdowns:** the System Optimization, Large File Finder, Memory Cleaner, Add Boot Operation and Duplicate File Finder dropdowns now have the rounded dark or light look of the Settings window instead of the plain white Windows box.
- **No more cut-off buttons in other languages:** buttons such as *+ Klasörü Hariç Tut* or *Tüm Seçimi Kaldır* in Turkish, or longer German and French labels, were clipped because their width was fixed for English. They now grow with their text, and the Malware Scan progress window is a little wider so its buttons fit in every language.

### Fixes
- **The Duplicate Files card in the main window follows Duplicate File Finder.** After deleting duplicates there, the card still said *2 duplicate copies of 2 files found (7.95 MB could be freed). Nothing was deleted.* It now shows how many copies were deleted and the space freed, and turns green, or names the files that could not be deleted.
- **The status under the Scan button follows the Malware Scan window too.** After a Malware Scan started from Utilities, removing or quarantining its threats updated the Malware Scan card but left *threats found* under the Scan button. It now shows the threats that remain.
- **"Cleaned" shows the space really freed again.** Since 1.27 the cleaned amount was the total size of the deleted files, which can be much more than the space drive C: actually gains (compressed or sparse files, hard links, cloud placeholders): for example 1.49 GB cleaned while drive C: gained 532 MB. It is again the difference in free space on C: before and after cleaning, as before 1.27, in the result line, the clean log and the "Your system cleaned!" notification. Programs writing to the disk at the same time (for example a download, or Windows at startup) can make it smaller. Because the free space can also change for other reasons (a program closed during cleaning may delete its own temporary files), the result line and the clean log now show the size of the deleted files on a line of its own as well, for example *Files deleted: 53 MB in 412 files.* next to *63 MB cleaned*.

### Updating
- **From 1.27.3 or earlier:** the app updates itself to 1.28 as usual (Settings > App Updates).
- **New installs:** download `mwc_setup.exe` (installer) or `mwc.zip` (portable) below.

---

For earlier updates see the [v1.27.3 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.27.3) (live scan status, every file checked by Malware Scan, LZMA uploads), the [v1.27.2 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.27.2) (no file counted twice), the [v1.27.1 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.27.1) (file types to scan, Quick Scan of the critical places, deep log filters), the [v1.27 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.27) (Disinfect, much faster cloud scans, per-file actions, secure deletion) and the [v1.26.6 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.6) (report wrong scan results). For signed database updates see the [v1.26.5 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.5), for signed app updates the [v1.26.4 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.4), and for the full list of what is new in 1.26 the [v1.26.3 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.3), the [v1.26.2 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.2), the [v1.26.1 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.1) and the [v1.26 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26).
