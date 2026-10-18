## Multron Win Cleaner 1.26.5 Update

> [!WARNING]
> **Antivirus false positives:** some antivirus programs may flag Multron Win Cleaner or one of its DLL files as suspicious when you download or run it. In particular, **Bitdefender** and engines that use it (for example Emsisoft, eScan, GData, Arcabit, VIPRE) may detect `MultronWinCleaner.dll` **heuristically as ransomware**, with a name such as `Gen:Heur.Ransom.Imps.3`. This is a false positive: a heuristic detection is based on behavior, not on a known malware signature, and it is triggered by what a system cleaner has to do (closing programs, deleting files, changing Windows settings). Multron Win Cleaner does not encrypt files, does not ask for a ransom and does not send your data anywhere except the optional cloud malware scan. The file has been reported to Bitdefender as a false positive again. The full source code is in this repository, and every release is built automatically by GitHub Actions. If your antivirus blocks the app, you can report the file to its vendor as a false positive or add an exception for the app folder.
>
> * Bitdefender false positive form: <https://www.bitdefender.com/consumer/support/answer/29358/>
> * Microsoft Defender file submission: <https://www.microsoft.com/en-us/wdsi/filesubmission>

> **Test release:** the cloud malware scan is published for testing. It is released so it can be tried thoroughly and made stable, so results and behavior may still change. Please report false detections, missed threats or any problems in [Issues](https://github.com/Multron-Community/MultronWcleaner/issues).

### New: Signed database updates
The cleaning database is now signed too, the same way app updates are signed since 1.26.4. The database decides which files are cleaned, so only a database published by the Multron Win Cleaner team is used.
- Every database release has an `update.json` with its version and the SHA-256 of `database.txt`, signed with the database release key (`update.json.sig`). The public key is built into the app; it is a separate key from the one that signs app updates.
- The downloaded `database.txt` must match its signed SHA-256 before it replaces the current database. A changed file is refused and the current database stays in use.
- The signed version must match the database release and be newer than the installed database, so an older database cannot be pushed as an update.
- Database updates are only accepted from the official GitHub releases.
- Database releases are checked without any secrets and signed in a separate, protected step, with a GitHub build attestation.

You can check a database download yourself with `node scripts/verify-update.js update.json update.json.sig database.txt` from the [database repository](https://github.com/Multron-Community/MultronWcleaner-Database).

### Changes
- A database update that is refused because it is older than the installed one now says so clearly.

### Updating
- **From 1.26.4 or earlier:** the app updates itself to 1.26.5 as usual (Settings > App Updates). App updates are checked against their signature since 1.26.4, and database updates from this version on.
- **New installs:** download `mwc_setup.exe` (installer) or `mwc.zip` (portable) below.

For signed app updates see the [v1.26.4 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.4), and for the full list of what is new in 1.26 the [v1.26.3 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.3), the [v1.26.2 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.2), the [v1.26.1 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.1) and the [v1.26 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26).
