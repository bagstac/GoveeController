# Plan: collapsible shortcut rows

UI-only change to the Shortcuts page (`src/GoveeController.Web/Components/Pages/Shortcuts.razor`
and `src/GoveeController.Web/wwwroot/app.css`). No domain, application, infrastructure, or
persistence changes. Written for another agent to execute.

## 1. Goal

Each row in the saved-shortcuts list becomes:

```
▸ Test off work
[Apply] [Edit] [Delete]
```

and when the name is clicked:

```
▾ Test off work
[Apply] [Edit] [Delete]
  ── Devices ──
  • Family Room R2 L
  • Family Room R2 R
  • Family Room R1 R
  Off
  → then Test work
```

Three requested changes, all in that one row template:

1. The always-visible detail line under the name goes away.
2. The name becomes an expander; its content moves into the expanded panel.
3. Apply / Edit / Delete move from the right-hand side to directly under the title.

## 2. Decisions already made (settled — implement as stated)

Answered directly by the repo owner. Do not relitigate.

| Question | Decision |
|---|---|
| What does the expanded panel show? | **Devices as a real list (one per line), then the power/brightness setting, then the chain link if present.** Nothing currently visible is lost — it is relocated and better formatted. |
| Multiple rows expanded at once? | **Yes, independent.** Each row toggles on its own; opening one does not close others. Not an accordion. |
| Where does the panel sit relative to the buttons? | **Title → buttons → panel.** Buttons sit directly under the title and never move when a row expands, so Apply/Edit/Delete stay in a stable position. |

## 3. Design decisions made in this plan

### 3.1 Use Blazor state, not `<details>`/`<summary>`

`<details>` would give keyboard and screen-reader behavior for free, and is the obvious first
instinct — **but it cannot produce the required layout.** `<details>` hides *every* child except
`<summary>` when collapsed, so putting the action buttons inside it would hide them when the row is
closed (violating decision 3), and putting them outside it forces the order title → panel → buttons
(violating decision 3 the other way).

So: track expansion in component state. Because that gives up the built-in accessibility, §4.2 below
specifies the ARIA and keyboard requirements explicitly — don't skip them.

### 3.2 Expansion state is a `HashSet<int>`, and is not persisted

`private readonly HashSet<int> _expandedShortcutIds = [];` — a set gives independent toggling
(decision 2) for free. Expansion is transient view state: it resets on page reload, and that's fine.
Do not persist it to the database or to local storage.

Stale ids left in the set after a shortcut is deleted are harmless (nothing looks them up), and
SQLite's `AUTOINCREMENT` primary key never reuses ids, so a stale entry can't accidentally re-expand
a different shortcut. Removing the id in `DeleteAsync` is optional tidiness, not a correctness fix.

### 3.3 The toggle wraps the caret and the name only — not the whole header

Clicking anywhere in a wide header strip to toggle is easy to hit by accident. Keep the clickable
target to the caret + name.

## 4. Implementation

### 4.1 Markup — replace the row template

Current (`Shortcuts.razor`, the `@foreach` inside `.shortcut-list`):

```razor
<div class="shortcut-row">
    <div>
        <strong>@shortcut.Name</strong>
        <div class="meta">
            @DeviceNamesFor(shortcut) &middot; @(shortcut.PowerOn ? "On" : "Off")@(shortcut.Brightness is { } b ? $" · {b}%" : "")@(ChainLabelFor(shortcut) is { } chainLabel ? $" · {chainLabel}" : "")
        </div>
    </div>
    <div>
        <button class="btn btn-primary" disabled="@_busy" @onclick="() => ApplyAsync(shortcut.Id)">
            @(_applyingId == shortcut.Id ? "Applying…" : "Apply")
        </button>
        <button class="btn" disabled="@_busy" @onclick="() => StartEdit(shortcut)">Edit</button>
        <button class="btn btn-danger" disabled="@_busy" @onclick="() => DeleteAsync(shortcut.Id)">Delete</button>
    </div>
</div>
```

Target shape (write it in the file's existing style; this is structure, not literal copy):

```razor
<div class="shortcut-row">
    <button type="button" class="shortcut-toggle"
            aria-expanded="@(IsExpanded(shortcut.Id) ? "true" : "false")"
            @onclick="() => ToggleExpanded(shortcut.Id)">
        <span class="shortcut-caret" aria-hidden="true">@(IsExpanded(shortcut.Id) ? "▾" : "▸")</span>
        <strong>@shortcut.Name</strong>
    </button>

    <div class="shortcut-actions">
        …the three existing buttons, unchanged…
    </div>

    @if (IsExpanded(shortcut.Id))
    {
        <div class="shortcut-details">
            <ul>
                @foreach (var name in DeviceNamesListFor(shortcut))
                {
                    <li>@name</li>
                }
            </ul>
            <div>@(shortcut.PowerOn ? "On" : "Off")@(shortcut.Brightness is { } b ? $" · {b}%" : "")</div>
            @if (ChainLabelFor(shortcut) is { } chainLabel)
            {
                <div>@chainLabel</div>
            }
        </div>
    }
</div>
```

**Preserve exactly**, do not "simplify" while moving them:

- `disabled="@_busy"` on all three buttons.
- `@(_applyingId == shortcut.Id ? "Applying…" : "Apply")` — this is the per-row busy label added in
  commit `32f9e4d`; it deliberately keys on `_applyingId`, **not** on `_busy`, so that other rows
  stay labeled "Apply" while disabled. Do not collapse it back to `_busy`.
- `type="button"` on the toggle. Without it, a `<button>` defaults to `type="submit"` — harmless
  here since the row is outside the `<EditForm>`, but set it anyway for the same reason the device
  picker trigger already does.

### 4.2 Accessibility (required, since §3.1 gave up `<details>`)

- The toggle **must** be a real `<button>`, not a `<div>`/`<span>` with `@onclick`. That's what makes
  it tabbable and Enter/Space-operable for free.
- `aria-expanded` must reflect current state (see markup above).
- The caret glyph is decorative — `aria-hidden="true"`, since the state is already conveyed by
  `aria-expanded`.

### 4.3 Code-behind

Add to the `@code` block, near the other view-state fields:

```csharp
// Which rows are expanded. A set (not a single int) because rows expand independently - opening
// one must not collapse another. Transient view state: deliberately not persisted.
private readonly HashSet<int> _expandedShortcutIds = [];

private bool IsExpanded(int shortcutId) => _expandedShortcutIds.Contains(shortcutId);

private void ToggleExpanded(int shortcutId)
{
    if (!_expandedShortcutIds.Add(shortcutId))
    {
        _expandedShortcutIds.Remove(shortcutId);
    }
}
```

(`HashSet.Add` returns false when the item was already present — that's the toggle.)

Add a helper returning the device names as a sequence rather than a joined string:

```csharp
/// <summary>Target device names for the expanded panel's list, one entry per device.</summary>
private IReadOnlyList<string> DeviceNamesListFor(Shortcut shortcut) => …
```

It should resolve each `ShortcutTarget` against `_devices`, falling back to the target's `DeviceSku`
when that device isn't in the loaded list — same as `DeviceNamesFor` does today.

**The `_devices is null` case needs a decision, not a copy.** `DeviceNamesFor` currently returns the
string `"{N} device(s)"` when the device list hasn't loaded yet — a count, not names, which doesn't
translate to a per-line list. Pick one and make it deliberate: either return that count string as a
single-element list, or fall back to listing the raw `DeviceSku` values. Either is defensible;
just don't return an empty list, which would render an empty `<ul>` and look broken.

**Two things to check before deleting `DeviceNamesFor`:**

1. It currently has exactly **one** call site — the meta line you're removing — so it will become
   unused. Delete it rather than leaving a near-duplicate of the new helper behind.
2. **`ChainLabelFor`'s XML doc contains `<see cref="DeviceNamesFor"/>`.** Deleting the method
   without updating that cref produces CS1574, which **fails the build** because
   `TreatWarningsAsErrors` is on in CI. Update that doc comment to point at the new helper (or drop
   the cross-reference) in the same change.

`ChainLabelFor`'s body is reused as-is — only its doc comment needs the cref fix above.

### 4.4 CSS — `app.css`

Replace the existing `.shortcut-row` rule (currently `display: flex; align-items: center;
justify-content: space-between;` — a horizontal layout that no longer applies) with a vertical stack,
and **delete the now-dead `.shortcut-row .meta` rule**, since no `.meta` element remains inside a
shortcut row. Note the `.meta` *class* is still used elsewhere on the page (the "Set either a color
or a color temperature" note under the form) — only the `.shortcut-row .meta` descendant selector
becomes dead.

Add rules for `.shortcut-toggle`, `.shortcut-caret`, `.shortcut-actions`, and `.shortcut-details`:

- `.shortcut-toggle` — reset the browser's button chrome (`background: none; border: none;
  padding: 0; font: inherit; color: inherit; cursor: pointer; text-align: left`) so it reads as
  plain text, and `display: flex` with a small gap to sit the caret beside the name.
- `.shortcut-actions` — `display: flex` with a gap. The three buttons currently have no explicit
  gap (they rely on inline whitespace); give them a real one. Add `flex-wrap: wrap` so they wrap
  rather than overflow on a narrow screen.
- `.shortcut-details` — muted small text matching the old `.meta` look (`color: #888;
  font-size: 0.85rem`), with a subtle top divider to separate it from the buttons. Tighten the
  `<ul>`'s default margin/padding so it doesn't look adrift.

Match the file's existing conventions: hex colours in the same family already used (`#888`, `#e2e2e2`,
`#eee`), `rem` spacing, and a comment only where the *why* isn't obvious (the button-chrome reset is
worth one).

## 5. Out of scope

- Any change to `ShortcutService`, the repository, the domain model, or the database.
- The "New shortcut / Edit shortcut" form below the list — untouched.
- The Devices page.
- Accordion behavior, persisting expansion state, or animating the expand/collapse.
- Sorting, filtering, or paging the shortcut list.

## 6. Verification

There is no component-test coverage for this page (bUnit was deliberately deferred —
`IMPROVEMENT-PLAN.md` §4.5), so the build and live checks *are* the verification. Do not treat "it
compiles" as done.

1. `dotnet build` — expect **0 warnings, 0 errors** (CI sets `TreatWarningsAsErrors`).
2. `dotnet test` — expect **76/76 passing, unchanged**. This change touches no tested logic; if the
   count moves, something unintended happened.
3. `docker compose up --build -d`, wait for healthy, then at <http://localhost:8080/shortcuts>:
   - Rows start **collapsed**, showing only the caret + name and the three buttons.
   - Clicking a name expands it; the device list, the power/brightness line, and the `→ then …`
     chain line all appear. Clicking again collapses it.
   - **Expand two rows at once** and confirm both stay open (decision 2).
   - Confirm the buttons do **not** move when a row expands (decision 3).
   - **Tab to a name and press Enter/Space** — it must toggle. This is the check that catches a
     `<div @onclick>` having been used instead of a `<button>`.
   - Apply a shortcut and confirm that row's button still reads "Applying…" while other rows'
     buttons stay reading "Apply" (the `_applyingId` behavior from `32f9e4d` must survive the move).
   - Edit and Delete still work from their new position.
   - At **375px width**, confirm the row still reads sensibly and the buttons wrap rather than
     overflow.
4. Check `docker logs` for unhandled exceptions.

Note `.env` holds a real Govee API key and this controls six real bulbs — applying a shortcut during
step 3 will visibly change them. That's expected and is how every feature in this repo has been
validated.

## 7. Repo context you'll need

- **Branch:** at time of writing, `main` does **not** have the two most recent commits — they're on
  `fix/review-findings` (`4abd1f9` docs, `32f9e4d` review fixes). Branch off whatever is currently
  checked out; do not merge anything to `main` yourself.
- **Conventions:** strict Clean Architecture layering (this change is Web-layer only, so it can't
  violate it); XML doc comments on public members; inline comments explain *why*, not *what*;
  detailed multi-paragraph commit messages ending with
  `Co-Authored-By: Claude <noreply@anthropic.com>`.
- Delete this file (`SHORTCUT-ROW-PLAN.md`) in the final commit; git history preserves it.
