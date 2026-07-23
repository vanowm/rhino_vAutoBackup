# Repository Instructions

- Derive every plug-in build version from the latest modified source `*.cs` file, using `yy.M.d.Hmm` with no seconds and non-padded month, day, and hour.
- For Rhino script files that define `VERSION`, use `yy.m.d.hmm` with no seconds, non-padded month/day/hour, and two-digit minutes; example: `26.7.8.1830`.
- Before every normal Release build, compose a specific behavioral summary of all changes since the last commit and supply it to `build.ps1 -Message`. The workspace build task must prompt for this message.
- Never generate or accept filename/category-only commit summaries such as `panel: update`, `plugin: update`, or `build: align release workflow`; the build must fail when its semantic message is missing or generic.
- Automatically commit and push only when a successful Release build changes the tracked Release DLL. Sign the commit, push `master`, and preserve the pending message if the commit fails.
- Use the `(No Commit)` workspace build task for a Release build that must not commit or push.
- Build versions shown in a README must be updated automatically by a successful Release build. Command versions in README command lists are introduction versions and must not change when commands are updated.
- Commit messages describe plug-in behavior and build changes; do not mention source script filenames.
- Keep paths relocatable. Do not embed machine-specific absolute paths in the plug-in or its runtime files.
- Undo/Redo command behavior should be implemented as hidden features. Do not show Undo or Redo as visible command-line options, and do not list them in visible command option sections.
