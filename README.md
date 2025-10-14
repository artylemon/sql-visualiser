# SQL Visualizer Web App

A web application for visualizing database dependencies, including tables and stored procedures, using directed and undirected graphs. 
Built with ASP.NET Core Razor Pages and .NET 8.

---

## Table of Contents

- [Features](#features)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Installation](#installation)
- [Usage](#usage)
  - [Analyzing a Database](#analyzing-a-database)
  - [Visualizing Dependencies](#visualizing-dependencies)
- [Detailed Feature Walkthrough](#detailed-feature-walkthrough)
  - [Intro / Overview](#intro--overview)
  - [Database Analysis](#database-analysis)
  - [Interactive Graphs](#interactive-graphs)
  - [Node Focus & Neighborhood Highlight](#node-focus--neighborhood-highlight)
  - [Previews (Definitions)](#previews-definitions)
- [Roadmap](#roadmap)
- [Technologies Used](#technologies-used)
- [Contributing](#contributing)
- [License](#license)

---

## Features

- **Database Analysis**: Retrieve catalogs from a SQL Server instance and construct a directed dependency graph.
- **Multi-Catalog (Inter-Database) Analysis**: Select multiple catalogs at once (multi-select control) to view cross-database edges.
- **Interactive Graph**: Zoom, pan, select nodes, and switch between multiple layouts (Dagre, Breadthfirst (radial), COSE (force), Circle, Grid).
- **Node Filtering & Depth Control**: Restrict visible subgraph by picking a root node and adjustable traversal depth.
- **Focus / Neighborhood Highlighting**: Click a node to fade non-neighbor nodes and emphasize its local neighborhood; click again or background to reset.
- **Definition Preview Panel**: When available, shows SQL definition / metadata of selected object with syntax highlighting (Prism.js).
- **Catalog Grouped Filtering**: Node dropdown grouped by catalog for quicker scoping.

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A SQL Server instance with accessible databases.
- A modern web browser (e.g., Chrome, Edge, Firefox).

### Installation

1. **Clone the Repository**:
  ```bash
git clone https://github.com/artylemon/sql-visualizer.git
cd sql-visualizer-webapp
  ```
2. **Restore Dependencies**:
  ```bash
dotnet restore
  ```
3. **Build the Project**:
  ```bash
dotnet build
  ```
4. **Run the Application**:
  ```bash
dotnet run
  ```
5. **Access the Application**:
   Open your browser and navigate to `http://localhost:5000`.

---

## Usage

### Analyzing a Database

1. Enter the server / data source (e.g. `FINTECHDB01-DEV,50003`).
2. Click **Retrieve Catalogs**.
3. Use the multi-select to choose one or more catalogs.
4. Click **Analyze Selected** to build the dependency graph.

### Visualizing Dependencies

- Nodes represent tables / procedures / functions (shape & color differentiate types).
- Directed edges show dependency direction.
- Layout, focus, and filtering controls let you refine the view (see walkthrough below).

---

## Detailed Feature Walkthrough

### Intro / Overview

Video:

### Database Analysis

Shows connecting, retrieving catalogs, selecting multiple, and launching analysis.

Videos:


https://github.com/user-attachments/assets/a392e2a9-04d9-4174-8388-b5f04883109f


https://github.com/user-attachments/assets/73da9a33-9426-4e1f-8856-0a2360169d22


Steps:
1. Provide data source.
2. Retrieve catalogs (lists all accessible catalogs; system catalogs included but visually distinct later).
3. Select desired catalogs.
4. Run analysis; a success message appears, then the graph renders.


### Interactive Graphs

Layout switching & depth filtering.

Videos:


https://github.com/user-attachments/assets/6cf0ae8c-5931-4188-af12-e13ddb14aaec


https://github.com/user-attachments/assets/f448ebaa-2dda-40cc-97b9-a194657ddd40



Key interactions:
- Zoom: mouse wheel / trackpad.
- Pan: drag background.
- Layout: select from dropdown (Dagre default; radial requires a specific node context so auto-falls-back if `Show All`).
- Node Filter: choose a root node or `Show All`.
- Depth: numerical control limits traversal distance from chosen root.
- System vs External: system catalog nodes have dotted borders; nodes outside analyzed set (when future external overlay is added) appear with alternate border style.

### Node Focus & Neighborhood Highlight

Focus is implicit—no extra button.

Usage:
1. Click a node: non-neighbors fade; direct neighborhood remains vivid; edges thicken color.
2. Depth control still applies—larger depth expands visible neighbors before focus highlight.
3. Click the same node again or empty background to clear focus.

### Previews (Definitions)

Displays SQL definition / metadata when backend supplies `definition` in graph payload.

Video:

https://github.com/user-attachments/assets/2b6d44ec-a329-46c7-90db-a22ddce92d6d



Contents (when available):
- Object name & catalog.
- Object type (table / procedure / function).
- Definition text (SQL) with Prism highlighting.
- (Future) Column / parameter lists & dependency summary at attribute level.

If a node lacks a definition, the panel remains hidden.

---

## Roadmap

Status legend: [x] done / available now, [~] partially implemented, [ ] planned.

Core Enhancements:
- [x] Multi-catalog (inter-database) analysis in single run.
- [x] Adjustable neighborhood depth filtering.
- [x] Multiple graph layouts (dagre, breadthfirst, cose, circle, grid).
- [x] Definition preview panel with SQL syntax highlighting (requires backend providing definitions).
- [x] Catalog-grouped node filter dropdown.
- [~] System vs external catalog styling (baseline; refinement & legend pending).
- [ ] Legend for edge / node semantics (reads vs writes vs calls, catalog color keys).
- [ ] Edge categorization & toggle controls (filter by relation type).
- [ ] Preview of logs / display of non-found entities like temp tables (currently only shown as warnings in logs).

Data & Lineage:
- [ ] Column-level lineage (track column propagation through procedures / functions).
- [ ] Incremental / delta analysis (only reprocess changed objects).

Interaction & UX:
- [ ] Advanced search (name / pattern / fuzzy) with highlight and temporary subgraph isolate.
- [ ] Export: PNG / SVG snapshot & JSON graph export.
- [ ] Accessibility: keyboard navigation for nodes, ARIA labels, color contrast improvements.

Platform & Integration:
- [ ] REST API endpoint for headless graph extraction (CI validation scenarios).
- [ ] Authentication options: SQL Auth & Azure AD (currently assumes environment / integrated).
- [ ] Configuration UI for layout defaults & performance parameters.

Performance & Quality:
- [ ] Virtualization / progressive rendering for very large graphs (>10k nodes) & background layouting.
- [ ] Caching layer for previously analyzed catalogs.
- [ ] Test coverage expansion: integration tests for parsing & graph building; UI-level tests (Playwright / Selenium) for baseline flows.

---

## Technologies Used

- **ASP.NET Core Razor Pages**: Web application framework.
- **Cytoscape.js**: Graph rendering & layout.
- **jQuery**: AJAX & DOM utilities.
- **Bootstrap**: UI components & layout.
- **Prism.js**: SQL syntax highlighting in definition preview.

---

## Contributing

Contributions are welcome! To contribute:

1. Fork the repository.
2. Create a new branch:
  ```bash
git checkout -b feature/your-feature-name
  ```
3. Commit your changes:
  ```bash
git commit -m "Add your descriptive message"
  ```
4. Push to your branch:
  ```bash
git push origin feature/your-feature-name
  ```
5. Open a pull request.

Please include before/after screenshots or GIFs for UI changes when possible.

---

## License

This project is licensed under the [MIT License](LICENSE).

---
