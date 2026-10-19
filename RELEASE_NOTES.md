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
