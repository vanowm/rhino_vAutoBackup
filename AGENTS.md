# Repository Instructions

- Derive every plug-in build version from the latest modified source `*.cs` file, using `yy.M.d.Hmm` with no seconds and non-padded month, day, and hour.
- Keep tracked text files on CRLF line endings through `.gitattributes` so builds do not emit Git conversion warnings.
- Route runtime diagnostics through the shared `Log` helper; keep the log beside the loaded DLL, clear it on startup, and record both plug-in and Rhino versions in the startup entry.
- Build and publish both `net7.0-windows` against Rhino 8 and `net10.0-windows` against Rhino 9; require both DLLs to succeed before the coordinated release commit, tag, and push.
- For Rhino script files that define `VERSION`, use `yy.m.d.hmm` with no seconds, non-padded month/day/hour, and two-digit minutes; example: `26.7.8.1830`.
- Immediately after every agent-made project change, create or refresh the pending summary with `build.ps1 -ComposeOnly -Message '<specific behavioral summary of all uncommitted changes>'`; do not defer this until build time. A manually launched workspace build must reuse that summary and prompt only when none exists.
- Never generate or accept filename/category-only commit summaries such as `panel: update`, `plugin: update`, or `build: align release workflow`; the build must fail when its semantic message is missing or generic.
- `build.ps1` without options must perform a standalone Release build with no Git requirement, commit, or push. Use `build.ps1 -Publish` or the normal workspace build task for the publishing flow.
- In the publishing flow, automatically commit and push only when a successful Release build changes the tracked Release DLL. Sign the commit, push `master`, and preserve the pending message if the commit fails.
- Use the `(No Commit)` workspace build task for a Release build that must not commit or push.
- Build versions shown in a README must be updated automatically by a successful Release build. Command versions in README command lists are introduction versions and must not change when commands are updated.
- Commit messages describe plug-in behavior and build changes; do not mention source script filenames.
- Keep paths relocatable. Do not embed machine-specific absolute paths in the plug-in or its runtime files.
- Undo/Redo command behavior should be implemented as hidden features. Do not show Undo or Redo as visible command-line options, and do not list them in visible command option sections.
