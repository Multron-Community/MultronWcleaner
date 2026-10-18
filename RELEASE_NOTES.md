## Multron Win Cleaner 1.26.4 Update

> [!WARNING]
> **Antivirus false positives:** some antivirus programs may flag Multron Win Cleaner or one of its DLL files as suspicious when you download or run it. In particular, **Bitdefender** and engines that use it (for example Emsisoft, eScan, GData, Arcabit, VIPRE) may detect `MultronWinCleaner.dll` **heuristically as ransomware**, with a name such as `Gen:Heur.Ransom.Imps.3`. This is a false positive: a heuristic detection is based on behavior, not on a known malware signature, and it is triggered by what a system cleaner has to do (closing programs, deleting files, changing Windows settings). Multron Win Cleaner does not encrypt files, does not ask for a ransom and does not send your data anywhere except the optional cloud malware scan. The file has been reported to Bitdefender as a false positive again. The full source code is in this repository, and every release is built automatically by GitHub Actions. If your antivirus blocks the app, you can report the file to its vendor as a false positive or add an exception for the app folder.
>
> * Bitdefender false positive form: <https://www.bitdefender.com/consumer/support/answer/29358/>
> * Microsoft Defender file submission: <https://www.microsoft.com/en-us/wdsi/filesubmission>

> **Test release:** the cloud malware scan is published for testing. It is released so it can be tried thoroughly and made stable, so results and behavior may still change. Please report false detections, missed threats or any problems in [Issues](https://github.com/Multron-Community/MultronWcleaner/issues).

### New: Signed updates
App updates are now signed, so only updates published by the Multron Win Cleaner team can be installed.
- Every release has an `update.json` that lists the SHA-256 of each file and is signed with the Multron Win Cleaner release key (`update.json.sig`). The public key is built into the app.
- The downloaded update must match its signed SHA-256 before it is unpacked. A file that was changed on GitHub without the private key is refused.
- The signed version must match the release and be newer than the installed version, so an older version cannot be pushed as an update.
- Updates are only accepted from the official GitHub releases.
- Releases are built without any secrets and signed in a separate, protected step that signs exactly what was built. Every file also has a GitHub build attestation that shows which workflow run and commit built it.

You can check any download yourself with `node scripts/verify-update.mjs update.json update.json.sig mwc.zip` from the repository, or `gh attestation verify mwc.zip --repo Multron-Community/MultronWcleaner`.

### Changes
- Multron Win Cleaner and its database moved to the [Multron Community](https://github.com/Multron-Community) organization on GitHub. The app now checks for updates and database updates there; older versions keep working through GitHub's redirect.

### Updating
- **From 1.26.3 or earlier:** the app updates itself to 1.26.4 as usual (Settings > App Updates). Every update after 1.26.4 is checked against the signature.
- **New installs:** download `mwc_setup.exe` (installer) or `mwc.zip` (portable) below.

For the full list of what is new in 1.26, see the [v1.26.3 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.3), the [v1.26.2 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.2), the [v1.26.1 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26.1) and the [v1.26 release](https://github.com/Multron-Community/MultronWcleaner/releases/tag/v1.26).
