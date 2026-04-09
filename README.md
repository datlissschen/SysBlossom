# 🌸 Cute  Windows Taskmanager

CuteTaskmanager is a specialized system monitor for Windows that blends professional performance tracking with a high-end "Kawaii" aesthetic. It transforms the often-sterile experience of process management into something visually delightful, interactive, and fun.
## Key Features

    Real-Time System Tracking: Live monitoring of CPU and RAM usage with animated graph visualizations.

    Interactive Mascot: A Lottie-animated mascot that reacts dynamically to system load (becomes "stressed" during high CPU usage).

    Sub-process Transparency: Detailed Flyout windows for each process group, showing individual PIDs, CPU, and Memory metrics.

    Advanced UI/UX:

        Kawaii Design: Rounded corners (25px), soft pastel color palettes, and custom "Heart" scrollbars.

        Contextual Information: Comprehensive ToolTips providing clarity on every system metric.

        Visual Feedback: "HeartBurst" animations when selecting processes.

    Smart Search & Sorting: Instant filtering and dynamic multi-column sorting that maintains scroll stability.

## 🎨 The Aesthetic ("Kawaii" Philosophy)

To achieve its unique look, the application utilizes several high-level XAML techniques:

### Bubbly Geometry: 
Extensive use of CornerRadius="25" on borders and buttons to create a soft, sticker-like appearance.

### Pastel Palette: 
A centralized ResourceDictionary defining a cohesive color scheme:
    | Color | Hex Code | Usage |
    | :--- | :--- | :--- |
    | Kawaii Pink | #FFD1DC | Primary highlights & Borders |
    | Kawaii Blue | #AEC6CF | CPU Graphs & Secondary accents |
    | Kawaii Purple | #E0BBE4 | Selection backgrounds & PIDs |
    | Cloud White | #FFF9F0 | Main application background |

    Lottie Integration: High-performance vector animations that provide a "living" interface without high CPU overhead.

## Technical Architecture

The project is built on the MVVM (Model-View-ViewModel) pattern, ensuring a clean separation between system logic and the user interface.
Performance Optimizations

### Threaded Icon Loading 
A dedicated background StartIconWorker extracts application icons asynchronously to prevent UI freezing.

###Concurrent Caching
A thread-safe IconCache using ConcurrentDictionary ensures icons are only extracted once per session, drastically reducing Disk I/O.

### UI Virtualization 
Optimized ListView handling with ItemsUpdatingScrollMode="KeepItemsInView" to prevent "jumping" during high-frequency data refreshes.

### Core Logic: CPU Calculation

We calculate CPU utilization per process by tracking the TotalProcessorTime over a specific interval, normalized across all logical cores:
CPU%=ΔRealTime×LogicalCoreCountΔProcessorTime​×100

## 📂 Project Structure
Plaintext

TaskManager/
├── Assets/             # Lottie JSON animations and static images
├── Helpers/            # IconHelper, IconCache, and UI utilities
├── Models/             # ProcessItem and SubProcessInfo data structures
├── Services/           # ProcessService (System API interactions)
├── ViewModels/         # MainViewModel (Logic, Timers, Filtering)
├── Views/              # MainWindow.xaml (The "Kawaii" Skin)
└── App.xaml            # Global styles and Pastel resources

## Getting Started

    Prerequisites: Windows 11 SDK (Version 10.0.22621.0 or higher) and Visual Studio 2022 with the "Windows App SDK" workload.

    Build: Open the solution and perform a Clean followed by a Rebuild.

    Run: Launch the application to start managing your processes with a touch of magic! ✨

    Note: This project is licensed under the MIT License. Feel free to contribute and make system monitoring even cuter! 🌸
