# findneedle

![Test Status](https://github.com/guscatalano/findneedle/actions/workflows/dotnet-desktop.yml/badge.svg?branch=master)
![Coverage](./badge.svg)

**A tool to quickly search through logs in Windows.**

---

## Overview

**findneedle** is a lightweight utility designed to help you efficiently search through log files on Windows systems. With a focus on speed and simplicity, it offers a clean user interface and powerful search capabilities.

## Features

- 🔍 Fast text search across large log files
- 🖥️ Simple, user-friendly interface
- 📂 Support for multiple log file formats
- ⏩ Real-time filtering and highlighting
- ⚡ Built with JavaScript, C#, HTML, and CSS

## Getting Started

### Prerequisites

- Windows OS
- [.NET Core SDK](https://dotnet.microsoft.com/download) (for C# components)

### Installation

1. **Clone the repository:**  
   `git clone https://github.com/guscatalano/findneedle.git`
2. **Navigate into the directory:**  
   `cd findneedle`
3. **Build or open the project:**
   - Open in Visual Studio or Visual Studio Code.
   - Build the solution using the .NET Core SDK.
4. **Run the application:**  
   - Launch the executable, or run via `dotnet run` as appropriate for your project.

#### Or, get it directly from the Microsoft Store:

[![Download from the Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-Findneedle-blue?logo=microsoft)](https://apps.microsoft.com/detail/9NWLTBV4NRDL?hl=en-us&gl=US&ocid=pdpshare)

## Usage

1. Launch the application.
2. Select or drag-and-drop log files.
3. Enter your search keyword(s).
4. View and filter results instantly.

## Architecture

### Plugin System

findneedle features a flexible plugin system that allows users to extend and customize how logs are processed and searched. Plugins can be developed independently and integrated with the application to add new functionality, such as custom log parsers, advanced filtering, or integration with external tools.

- **How it works:**  
  Plugins are loaded at runtime and can interact with the application via well-defined interfaces. This enables developers to add or modify features without changing the core codebase.

- **Creating a Plugin:**  
  To create a plugin, implement the required interface (see the `/plugins` directory for examples) and register your plugin with the application. The system will automatically detect and initialize available plugins on startup.

### Input, Output, and Processor System

findneedle is architected around three main extensible component types: Inputs, Outputs, and Processors.

#### Inputs

Inputs are responsible for acquiring data (such as log files) and feeding them into the processing pipeline. Examples include reading from local files, directories, or even remote sources.

#### Processors

Processors take the raw input data and apply transformations or analyses. This can include parsing, filtering, or enriching log entries. Multiple processors can be chained to build complex processing pipelines.

#### Outputs

Outputs handle the final presentation or export of processed data.

