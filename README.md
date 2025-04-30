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
- [Technologies Used](#technologies-used)
- [Contributing](#contributing)
- [License](#license)

---

## Features

- **Database Analysis**: Analyze database catalogs to identify dependencies between tables and stored procedures.
- **Directed and Undirected Graphs**: Visualize database relationships using directed and undirected graphs.
- **Interactive Graphs**: Explore dependencies interactively with zoom, pan, and node/edge highlighting.

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

1. Enter the connection string for your SQL Server instance in the "Enter Data Source" field.
2. Click **Retrieve Catalogs** to fetch available databases.
3. Select a catalog from the dropdown and click **Analyze Selected Catalog**.

### Visualizing Dependencies

- The application generates a dependency graph showing relationships between tables and stored procedures.
- Use the interactive graph to explore:
  - **Nodes**: Represent tables and stored procedures.
  - **Edges**: Represent relationships (e.g., reads, writes, calls).

---

## Technologies Used

- **ASP.NET Core Razor Pages**: For building the web application.
- **Cytoscape.js**: For rendering interactive dependency graphs.
- **SQL Server**: For database analysis.
- **jQuery**: For AJAX requests and DOM manipulation.
- **Bootstrap**: For responsive UI design.

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
git checkout -b feature/your-feature-name
  ```
4. Push to your branch:
  ```bash
git push origin feature/your-feature-name
  ```
5. Open a pull request.

---

## License

This project is licensed under the [MIT License](LICENSE).

---

   
