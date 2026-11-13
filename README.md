# Simon Flow

**Talk, and it types.** Press a shortcut, say what you want, and Simon Flow types clean text wherever your cursor is: Discord, Google Docs, email, anywhere on Windows.

**[Download for Windows](https://simon-margalit.github.io/simon-flow/)** · Free · No account needed

## What it does

- **Works in any app.** If you can type there, you can talk there.
- **Cleans up what you say.** Removes "um" and "uh" and fixes capitals and punctuation.
- **Runs on your computer.** Speech recognition uses [whisper.cpp](https://github.com/ggerganov/whisper.cpp) locally, so your voice isn't sent anywhere.
- **Optional AI cleanup.** Add a Claude API key for smarter rewriting, five styles (Natural, Concise, Formal, Email, Bullets) and translation into 16 languages.
- **Little extras.** Hold-to-talk, Esc to cancel, a microphone picker, start/stop sounds, a words-per-minute counter and a small on-screen pill that shows when it's listening.

## Getting started

1. Download `SimonFlow.zip` from the [website](https://simon-margalit.github.io/simon-flow/) and unzip it.
2. Double-click **Build and Open Simon Flow.bat**. The first time, it downloads the speech engine (about 150 MB), builds the app and adds a desktop shortcut.
3. Click into any text box, press **Ctrl + Alt + Space**, talk, and press it again when you're done.

Windows may show "Windows protected your PC" because this is a small, unsigned app. Click **More info**, then **Run anyway**.

## How it's built

- One C# file (`SimonFlow.cs`), Windows Forms, .NET Framework 4. It's compiled on your PC by the included `.bat` file using the C# compiler that comes with Windows.
- Records the microphone with the Windows `waveIn` API and transcribes with whisper.cpp (`base.en` model).
- Optional cleanup step calls the Claude API with your own key.

## Who made it

Made by a dad and his son. We came up with the idea, designed it, and tested it ourselves, and used Claude to help write the code.

## License

[MIT](LICENSE)
