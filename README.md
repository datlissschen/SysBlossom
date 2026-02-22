# CuteTaskmanager
A taskmanager that makes working with it more fun and cute.

##✨ Making it "Kawaii" (The Design)

###To achieve that specific aesthetic, you’ll want to focus on these three XAML-based techniques:

  Corner Radius: In WinUI 3, you can set CornerRadius="15" on almost everything to get those soft, bubbly edges.

  Color Palette: Use a Resource Dictionary to define a palette of Pastels (e.g., #FFB7B2 for soft pink, #B2E2F2 for sky blue).

  Lottie Animations: Use the LottieVisualSource library to add small, animated characters that react when a process is "killed" (e.g., a small cat waving goodbye).

##💡 Pro Tip for Icons

Since a Task Manager needs icons for every app (Chrome, Spotify, etc.), you can extract them in C# using Icon.ExtractAssociatedIcon(processPath). To make them "pretty," you can display them inside a Border with a heavy CornerRadius and a soft DropShadow to make them look like stickers!

## logic for the process use
 we use the method TotalProcessorTime. We track all of the processors with it
CPU %= (ΔProzesszeit / (ΔEchtzeit×Anzahl logischer Kerne )) ​×100

##📂 Recommended Project Structure

#For a Task Manager, I recommend a simple MVVM (Model-View-ViewModel) structure. It sounds fancy, but it just keeps your "Pretty" code separate from your "System" code:

   Models: ProcessModel.cs (Holds the name, icon, and CPU usage).

   ViewModels: MainViewModel.cs (The "Brain" that refreshes the list every 1 second).

   Views: MainWindow.xaml (The "Kawaii" skin).
