# TTMS Jira-Style UI/UX Design Standards

This document establishes the official UI/UX design standards for the TTMS application. It provides design rules, color specifications, component patterns, and stylesheet guidelines to ensure visual consistency and a premium, Jira-like user experience.

---

## 1. Design Principles

- **Sleek & Uncluttered**: Reduce visual noise. Prefer borders and subtle fills over heavy gradients or solid dark backgrounds.
- **Side-by-Side Productivity**: Maximize viewport utility by splitting views into action/content columns (left) and metadata/settings panels (right).
- **Responsive Feedback**: Every interaction (hover, status toggle, form change) must have smooth transitions (150–200ms) and visual indicators.
- **Accurate Colors**: Use designated Jira/Atlassian-equivalent hex codes and status styles.

---

## 2. Global CSS Custom Variables

All pages should utilize these CSS custom properties (defined in `wwwroot/css/task-detail.css`):

```css
:root {
    /* Colors */
    --jira-blue: #0052CC;
    --jira-blue-hover: #0065FF;
    --jira-blue-subtle: #DEEBFF;
    --jira-text: #172B4D;
    --jira-text-muted: #5E6C84;
    --jira-bg-subtle: #F4F5F7;
    --jira-bg-hover: #EBECF0;
    --jira-border: #DFE1E6;
    --jira-border-focus: #4C9AFF;
    
    /* Layout */
    --jira-radius: 4px;
    --jira-radius-lg: 8px;
    --transition-speed: 0.15s;
}
```

---

## 3. Core Design Patterns & Components

### 3.1. Page Layout
For detail/edit pages, use a two-column responsive split:
- **Left Column (`.task-main-col`)**: Takes 70-75% width. Contains breadcrumbs, page title, quick actions toolbar, primary text/descriptions, and tabs.
- **Right Column (`.task-side-col`)**: Takes 25-30% width. Sticky placement (`top: 80px`), clean grey card background (`#FAFBFC`), and holds status, time-tracking, and other metadata fields.

### 3.2. Status Badges & Dropdowns
Status styling must strictly match Atlassian's palette. Badges should be uppercase, bold, and padded:

| Status | Background Color | Text Color | Meaning |
| :--- | :--- | :--- | :--- |
| **TODO** | `#DFE1E6` | `#42526E` | Backlog or not started |
| **IN PROGRESS** | `#DEEBFF` | `#0052CC` | Active work |
| **PENDING** | `#FFF0B3` | `#172B4D` | Waiting on external factor |
| **BLOCKED** | `#FFEBE6` | `#DE350B` | Impediment present |
| **DONE** | `#E3FCEF` | `#006644` | Completed |
| **CANCELLED** | `#F4F5F7` (dashed border) | `#8993A4` | Terminated or obsolete |

#### CSS Classes:
Use `.status-todo`, `.status-inprogress`, `.status-pending`, `.status-blocked`, `.status-done`, and `.status-cancelled`.

---

### 3.3. Priority Indicators
Prefer clean, graphic representations over simple badges:
- **Critical**: Double chevron up (`bi-chevron-double-up` in red `#DE350B`).
- **High**: Single chevron up (`bi-chevron-up` in orange-red `#FF5630`).
- **Medium**: Single chevron up (`bi-chevron-up` in yellow/orange `#FFAB00`).
- **Low**: Single chevron down (`bi-chevron-down` in blue `#0052CC`).
- **None**: Dash icon (`bi-dash` in gray).

---

### 3.4. Time Tracking Progress Bars
Progress bars must represent estimated hours vs. logged hours visually:
- **Normal Progress**: A blue bar (`#0052CC`) representing logged time percentage.
- **Over-Budget warning**: If actual logged hours exceed estimated hours, the progress bar should immediately shift to red (`#DE350B`) to highlight scope creep.

---

### 3.5. Initials-Based Avatars
To provide high-fidelity user identities:
- Generate a circular badge containing the user's first and last initials (e.g. "Jane Doe" -> "JD").
- Rotate background colors dynamically based on the name hash.
- Colors should be soft, professional slate tones (e.g., Atlassian slate blue, emerald green, dark navy, deep violet, dark coral, turquoise).

---

### 3.6. Filter Sections (Interactive Pills)
Replace standard checklists and selects with horizontal filter pill decks:
- Hide original checkbox inputs using `.btn-check`.
- Style sibling labels (`.filter-pill`) as rounded buttons with light gray borders.
- On checking, dynamically apply the corresponding Status or Priority background color.
- Auto-submit changes client-side for a responsive, reactive feel.

---

## 4. Pre-Commit Verification Checklist for UI Changes

When implementing new pages or widgets:
1. **No generic colors**: Never use pure green (`#00FF00`), pure red (`#FF0000`), or pure blue (`#0000FF`).
2. **Transition effects**: Ensure all hover and focus actions transitions use a smooth `transition: all 0.15s ease`.
3. **Responsive alignment**: Verify columns collapse cleanly on viewports smaller than `992px`.
4. **Interactive cursor**: Add `cursor: pointer` to all custom selectors, pills, and dropdowns.
