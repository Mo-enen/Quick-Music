using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Raylib_cs;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using System.Threading;


// ======== Init ========

string SavingFolder = Util.CombinePaths(Environment.GetFolderPath(
	Environment.SpecialFolder.LocalApplicationData), "Moenen", "QuickMusic"
);
Util.CreateFolder(SavingFolder);
string ConfigPath = Util.CombinePaths(SavingFolder, "Config.txt");
int WindowWidth = 1000;
int WindowHeight = 1000;
int PlayingListIndex = -1;
int PlayingMusicIndex = -1;
var GlobalRan = new Random((int)DateTime.Now.Ticks);
// Load Config
{
	foreach (string line in Util.ForAllLinesInFile(ConfigPath, Encoding.UTF8)) {
		int cIndex = line.IndexOf(':');
		if (cIndex < 0 || cIndex + 1 >= line.Length) continue;
		if (line.StartsWith("WindowWidth:") && int.TryParse(line[(cIndex + 1)..], out int width)) {
			WindowWidth = width;
			continue;
		}
		if (line.StartsWith("WindowHeight:") && int.TryParse(line[(cIndex + 1)..], out int height)) {
			WindowHeight = height;
			continue;
		}
		if (line.StartsWith("LastPlayingList:") && int.TryParse(line[(cIndex + 1)..], out int index)) {
			PlayingListIndex = index;
			continue;
		}
	}
}
WindowWidth = Math.Clamp(WindowWidth, 200, 4000);
WindowHeight = Math.Clamp(WindowHeight, 200, 4000);



List<PlayList> PlayLists = [];
string PlayListRoot = Util.CombinePaths(SavingFolder, "PlayList");
Util.CreateFolder(PlayListRoot);
// Load PlayLists
{
	foreach (var rootFolder in Util.EnumerateFolders(Environment.CurrentDirectory, true)) {
		var pList = new PlayList() { Name = Util.GetNameWithoutExtension(rootFolder) };
		foreach (var musicPath in Util.EnumerateFiles(rootFolder, false, "*.mp3", "*.wav", "*.ogg", "*.flac", "*.mod", "*.xm")) {
			pList.Musics.Add((musicPath, Util.GetNameWithoutExtension(musicPath)));
		}
		if (pList.Musics.Count == 0) continue;
		pList.Musics.Sort((a, b) => a.name.CompareTo(b.name));
		PlayLists.Add(pList);
	}

	PlayLists.Sort((a, b) => a.Name.CompareTo(b.Name));
}




// ======== Running ========

Music CurrentMusic = default;
bool RequireMusicPlaying = false;
int PlayListScrollY = 0;
int MusicListScrollY = 0;
float TimePlayed = 0f;
float CurrentMusicDuration = 0.1f;
int UiListIndex = -1;
bool SeekingMusic = false;
bool PlayAfterSeek = false;
Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
Raylib.SetWindowState(ConfigFlags.AlwaysRunWindow | ConfigFlags.UnfocusedWindow | ConfigFlags.ResizableWindow | ConfigFlags.MinimizedWindow);
Raylib.InitWindow(WindowWidth, WindowHeight, "Quick Music");
Raylib.SetTargetFPS(48);
Raylib.InitAudioDevice();
#if !DEBUG
Raylib.MinimizeWindow();
RequireMusicPlaying = true;
#endif
var DefaultFont = Raylib.GetFontDefault();
var assembly = Assembly.GetExecutingAssembly();
var stream = assembly.GetManifestResourceStream("QuickMusic.Icon.png");
if (stream != null) {
	using (stream)
	using (var reader = new BinaryReader(stream)) {
		var pngBytes = reader.ReadBytes((int)stream.Length);
		var img = Raylib.LoadImageFromMemory(".png", pngBytes);
		Raylib.SetWindowIcon(img);
		Raylib.UnloadImage(img);
	}
}

// Start First Music
{
	if (PlayMusic(GetRandomMusicPath(PlayingListIndex, true, out int _playingListIndex))) {
		PlayingListIndex = _playingListIndex;
		UiListIndex = _playingListIndex;
		PlayingMusicIndex = 0;
	}
	FocusScrollToPlayingMusic();
}


while (!Raylib.WindowShouldClose()) {

	// Update Music
	Raylib.UpdateMusicStream(CurrentMusic);

	// Music Logic
	if (Raylib.IsMusicReady(CurrentMusic)) {

		// Update Music State
		bool isPlaying = Raylib.IsMusicStreamPlaying(CurrentMusic);
		if (isPlaying && !RequireMusicPlaying) {
			// Pause
			Raylib.PauseMusicStream(CurrentMusic);
		}
		if (!isPlaying && RequireMusicPlaying) {
			// Resume
			Raylib.ResumeMusicStream(CurrentMusic);
		}

		// Check Music End
		TimePlayed = Raylib.GetMusicTimePlayed(CurrentMusic);
		CurrentMusicDuration = Raylib.GetMusicTimeLength(CurrentMusic);
		if (TimePlayed > CurrentMusicDuration - 0.2f) {
			// Next
			var musics = PlayLists[PlayingListIndex].Musics;
			int nextIndex = (PlayingMusicIndex + 1) % musics.Count;
			if (PlayMusic(musics[nextIndex].path)) {
				PlayingMusicIndex = nextIndex;
			}
			FocusScrollToPlayingMusic();
			TimePlayed = 0f;
		}
	} else {
		TimePlayed = 0f;
		CurrentMusicDuration = 0.1f;
	}

	// UI
	if (!Raylib.IsWindowMinimized()) {

		const int BOTTOM_BAR_HEIGHT = 64;
		const int ITEM_HEIGHT = 40;
		const int ITEM_PADDING = 6;
		int screenWidth = Raylib.GetScreenWidth();
		int screenHeight = Raylib.GetScreenHeight();
		WindowWidth = screenWidth;
		WindowHeight = screenHeight;
		var mousePos = Raylib.GetMousePosition();
		bool mouseLeftPressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
		bool mouseLeftHolding = Raylib.IsMouseButtonDown(MouseButton.Left);
		bool mouseInPlaylist = mousePos.Y < screenHeight - BOTTOM_BAR_HEIGHT && mousePos.X < screenWidth / 3;
		bool mouseInMusic = mousePos.Y < screenHeight - BOTTOM_BAR_HEIGHT && mousePos.X >= screenWidth / 3;
		Raylib.BeginDrawing();

		// Play List
		if (PlayLists.Count > 0) {
			int pageCount = (screenHeight - BOTTOM_BAR_HEIGHT) / (ITEM_HEIGHT + ITEM_PADDING);
			PlayListScrollY = Math.Clamp(PlayListScrollY, 0, Math.Max(PlayLists.Count - pageCount + 2, 0));
			Raylib.DrawRectangle(0, 0, screenWidth / 3, screenHeight, Color.Black);
			for (int i = PlayListScrollY; i < PlayLists.Count; i++) {
				int y = 20 + (i - PlayListScrollY) * (ITEM_HEIGHT + ITEM_PADDING);
				// Highlight
				if (mouseInPlaylist && mousePos.Y > y && mousePos.Y <= y + ITEM_HEIGHT + ITEM_PADDING) {
					Raylib.DrawRectangle(
						20, y, screenWidth / 3,
						ITEM_HEIGHT + ITEM_PADDING, new Color(22, 22, 22, 255)
					);
					// Click
					if (mouseLeftPressed) {
						bool playRandomMusic = UiListIndex == i;
						UiListIndex = i;
						if (playRandomMusic) {
							string path = GetRandomMusicPath(i, false, out _);
							if (PlayMusic(path)) {
								PlayingListIndex = UiListIndex;
								PlayingMusicIndex = 0;
								FocusScrollToPlayingMusic();
								PlayListScrollY = Math.Clamp(PlayListScrollY, 0, Math.Max(PlayLists.Count - pageCount + 6, 0));
							}
						}
					}
				}
				// Label
				Raylib.DrawText(
					PlayLists[i].Name, 20, y, ITEM_HEIGHT,
					UiListIndex == i ? Color.Green : Color.RayWhite
				);
			}
		} else {
			// No PlayList Hint
			Raylib.DrawText(
				"No Music Founded\n\n\n\n\nTo Add Music:\n\n\n    Step 1: Create a Folder Next to This App's Exe File.\n\n\n    Step 2: Put Music Files into That Folder.\n\n\n    Step 3: Restart this App.",
				64, 64, 32, Color.Gray
			);
		}

		// Music
		if (PlayLists.Count > 0) {
			Raylib.DrawRectangle(screenWidth / 3, 0, screenWidth * 2 / 3, screenHeight, Color.Black);
		}
		if (UiListIndex >= 0 && UiListIndex < PlayLists.Count) {
			var list = PlayLists[UiListIndex];
			if (list.Musics.Count > 0) {
				int pageCount = (screenHeight - BOTTOM_BAR_HEIGHT) / (ITEM_HEIGHT + ITEM_PADDING);
				MusicListScrollY = Math.Clamp(MusicListScrollY, 0, Math.Max(list.Musics.Count - pageCount + 2, 0));
				for (int i = MusicListScrollY; i < list.Musics.Count; i++) {
					var (path, name) = list.Musics[i];
					int y = 20 + (i - MusicListScrollY) * (ITEM_HEIGHT + ITEM_PADDING);
					// Highlight
					if (mouseInMusic && mousePos.Y > y && mousePos.Y <= y + ITEM_HEIGHT + ITEM_PADDING) {
						Raylib.DrawRectangle(
							screenWidth / 3 + 20, y, screenWidth * 2 / 3,
							ITEM_HEIGHT + ITEM_PADDING, new Color(22, 22, 22, 255)
						);
						// Click
						if (mouseLeftPressed) {
							if (PlayMusic(path)) {
								PlayingListIndex = UiListIndex;
								PlayingMusicIndex = i;
							}
						}
					}
					// Label
					Raylib.DrawText(
						name, screenWidth / 3 + 20, y, ITEM_HEIGHT,
						PlayingListIndex == UiListIndex && PlayingMusicIndex == i ? Color.Green : Color.RayWhite
					);
				}
			}
		}

		if (PlayLists.Count > 0) {
			// Bottom Bar
			var barBgColor = new Color(24, 24, 24, 255);
			var barBtnHighlightColor = new Color(32, 32, 32, 255);
			int barTop = screenHeight - BOTTOM_BAR_HEIGHT;

			// Play/Pause
			int ppL = 0;
			int ppT = barTop;
			int ppW = BOTTOM_BAR_HEIGHT;
			int ppH = BOTTOM_BAR_HEIGHT;
			bool ppBtnHovering = Raylib.IsCursorOnScreen() && mousePos.X > ppL && mousePos.X < ppL + ppW && mousePos.Y > ppT && mousePos.Y < ppT + ppH;
			Raylib.DrawRectangle(ppL, ppT, ppW, ppH, ppBtnHovering ? barBtnHighlightColor : barBgColor);
			if (RequireMusicPlaying) {
				Raylib.DrawTextPro(
					DefaultFont, "=",
					new(ppL + ppW * 0.45f, ppT + ppH * 0.75f),
					new(ppW / 2f, ppH / 2f),
					90, ppH, 0, Color.RayWhite
				);
			} else {
				int triL = ppL + ppW * 4 / 10;
				int triR = ppL + ppW * 7 / 10;
				int triD = ppT + ppH * 3 / 10;
				int triU = ppT + ppH * 7 / 10;
				Raylib.DrawTriangle(
					new(triL, triD),
					new(triL, triU),
					new(triR, (triD + triU) / 2),
					Color.RayWhite
				);
			}
			if (ppBtnHovering && mouseLeftPressed) {
				RequireMusicPlaying = !RequireMusicPlaying;
			}
			if (Raylib.IsKeyPressed(KeyboardKey.Space)) {
				RequireMusicPlaying = !RequireMusicPlaying;
			}

			// Progress Bar
			int pBarL = ppW;
			int pBarT = barTop;
			int pBarW = screenWidth - ppW;
			int pBarH = BOTTOM_BAR_HEIGHT;
			bool pBarHovering = Raylib.IsCursorOnScreen() && mousePos.X > pBarL && mousePos.X < pBarL + pBarW && mousePos.Y > pBarT && mousePos.Y < pBarT + pBarH;
			Raylib.DrawRectangle(pBarL, pBarT, pBarW, pBarH, barBgColor);
			Raylib.DrawRectangle(pBarL, pBarT, (int)(pBarW * TimePlayed / MathF.Max(CurrentMusicDuration, 0.1f)), pBarH, new Color(24, 42, 24, 255));
			if (pBarHovering && mouseLeftHolding) {
				// Seek
				if (!SeekingMusic) {
					SeekingMusic = true;
					PlayAfterSeek = RequireMusicPlaying;
				}
				RequireMusicPlaying = false;
				float newProgress01 = Math.Clamp((mousePos.X - pBarL) / pBarW, 0f, 1f);
				if (Raylib.IsMusicReady(CurrentMusic)) {
					Raylib.SeekMusicStream(CurrentMusic, newProgress01 * CurrentMusicDuration);
				}
			} else {
				// Not Seeking
				if (SeekingMusic) {
					SeekingMusic = false;
					RequireMusicPlaying = PlayAfterSeek;
				}
			}

			// Scroll with Mouse Wheel
			float wheel = Raylib.GetMouseWheelMove();
			if (Math.Abs(wheel) > 0.1f) {
				if (mousePos.X < screenWidth / 3) {
					// For Play List
					PlayListScrollY -= (int)wheel;
				} else {
					// For Music List
					MusicListScrollY -= (int)wheel;
				}
			}
		}

	}

	// Finish
	Raylib.EndDrawing();

}




// ======== Quit ========

// Save Config
{
	var builder = new StringBuilder();
	if (!Raylib.IsWindowMinimized()) {
		builder.AppendLine($"WindowWidth:{Raylib.GetScreenWidth()}");
		builder.AppendLine($"WindowHeight:{Raylib.GetScreenHeight()}");
	} else {
		builder.AppendLine($"WindowWidth:{WindowWidth}");
		builder.AppendLine($"WindowHeight:{WindowHeight}");
	}
	builder.AppendLine($"LastPlayingList:{PlayingListIndex}");
	Util.TextToFile(builder.ToString(), ConfigPath, Encoding.UTF8);
}


string GetRandomMusicPath (int listIndex, bool failbackToOtherPlist, out int resultListIndex) {

	resultListIndex = -1;
	if (PlayLists.Count == 0) return "";

	if (listIndex < 0 || listIndex >= PlayLists.Count) {
		listIndex = GlobalRan.Next(0, PlayLists.Count);
	}
	listIndex = Math.Clamp(listIndex, 0, PlayLists.Count - 1);

	var list = PlayLists[listIndex];
	int musicCount = list.Musics.Count;
	if (musicCount > 0) {
		resultListIndex = listIndex;
		// Shuffle Musics
		if (musicCount > 1) {
			var musics = list.Musics;
			for (int i = 0; i < musicCount; i++) {
				int ranIndex = GlobalRan.Next(i, musicCount);
				(musics[i], musics[ranIndex]) = (musics[ranIndex], musics[i]);
			}
		}
		return list.Musics[0].path;
	} else if (failbackToOtherPlist) {
		// Failback
		for (int i = 0; i < PlayLists.Count; i++) {
			var _list = PlayLists[i];
			if (_list.Musics.Count > 0) {
				resultListIndex = i;
				return GetRandomMusicPath(i, false, out _);
			}
		}
	}
	return "";
}


bool PlayMusic (string path) {
	if (!Util.FileExists(path)) return false;
	if (Raylib.IsMusicReady(CurrentMusic)) {
		if (Raylib.IsMusicStreamPlaying(CurrentMusic)) {
			Raylib.StopMusicStream(CurrentMusic);
		}
		Raylib.UnloadMusicStream(CurrentMusic);
	}
	CurrentMusic = Raylib.LoadMusicStream(path);
	Raylib.PlayMusicStream(CurrentMusic);
	Raylib.SetWindowTitle(" " + Util.GetNameWithoutExtension(path));
	return true;
}


void FocusScrollToPlayingMusic () {
	if (PlayingListIndex != UiListIndex) return;
	PlayListScrollY = PlayingListIndex - 3;
	MusicListScrollY = PlayingMusicIndex - 3;
}


// ============ Class ============

public class PlayList {
	public string Name = "";
	public readonly List<(string path, string name)> Musics = [];
}

static class Util {

	// File
	public static void TextToFile (string data, string path, Encoding encoding, bool append = false) {
		CreateFolder(GetParentPath(path));
		using FileStream fs = new(path, append ? FileMode.Append : FileMode.Create);
		using StreamWriter sw = new(fs, encoding);
		sw.Write(data);
		fs.Flush();
		sw.Close();
		fs.Close();
	}


	public static IEnumerable<string> ForAllLinesInFile (string path, Encoding encoding) {
		if (!FileExists(path)) yield break;
		using StreamReader sr = new(path, encoding);
		while (sr.Peek() >= 0) yield return sr.ReadLine();
	}


	public static void CreateFolder (string path) {
		if (string.IsNullOrEmpty(path) || FolderExists(path)) return;
		string pPath = GetParentPath(path);
		if (!FolderExists(pPath)) {
			CreateFolder(pPath);
		}
		Directory.CreateDirectory(path);
	}


	public static IEnumerable<string> EnumerateFiles (string path, bool topOnly, string searchPattern) {
		if (!FolderExists(path)) yield break;
		var option = topOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
		foreach (string str in Directory.EnumerateFiles(path, searchPattern, option)) {
			yield return str;
		}
	}
	public static IEnumerable<string> EnumerateFiles (string path, bool topOnly, params string[] searchPatterns) {
		if (!FolderExists(path)) yield break;
		var option = topOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
		if (searchPatterns == null || searchPatterns.Length == 0) {
			foreach (var filePath in Directory.EnumerateFiles(path, "*", option)) {
				yield return filePath;
			}
		} else {
			foreach (var pattern in searchPatterns) {
				foreach (var filePath in Directory.EnumerateFiles(path, pattern, option)) {
					yield return filePath;
				}
			}
		}
	}


	public static IEnumerable<string> EnumerateFolders (string path, bool topOnly, string searchPattern = "*") {
		if (!FolderExists(path)) yield break;
		var option = topOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
		foreach (string str in Directory.EnumerateDirectories(path, searchPattern, option)) {
			yield return str;
		}
	}


	public static bool CopyFolder (string from, string to, bool copySubDirs, bool ignoreHidden, bool overrideFile = false) {

		// Get the subdirectories for the specified directory.
		DirectoryInfo dir = new(from);

		if (!dir.Exists) return false;

		DirectoryInfo[] dirs = dir.GetDirectories();
		// If the destination directory doesn't exist, create it.
		if (!Directory.Exists(to)) {
			Directory.CreateDirectory(to);
		}

		// Get the files in the directory and copy them to the new location.
		FileInfo[] files = dir.GetFiles();
		foreach (FileInfo file in files) {
			try {
				string tempPath = Path.Combine(to, file.Name);
				if (!ignoreHidden || (file.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden) {
					file.CopyTo(tempPath, overrideFile);
				}
			} catch { }
		}

		// If copying subdirectories, copy them and their contents to new location.
		if (copySubDirs) {
			foreach (DirectoryInfo subdir in dirs) {
				try {
					string temppath = Path.Combine(to, subdir.Name);
					if (!ignoreHidden || (subdir.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden) {
						CopyFolder(subdir.FullName, temppath, copySubDirs, ignoreHidden, overrideFile);
					}
				} catch { }
			}
		}
		return true;
	}


	// Path
	public static string GetParentPath (string path) {
		if (string.IsNullOrEmpty(path)) return "";
		var info = Directory.GetParent(path);
		return info != null ? info.FullName : "";
	}


	public static string CombinePaths (string path1, string path2) => Path.Combine(path1, path2);
	public static string CombinePaths (string path1, string path2, string path3) => Path.Combine(path1, path2, path3);


	public static string GetExtensionWithDot (string path) => Path.GetExtension(path);//.txt


	public static string GetNameWithoutExtension (string path) => Path.GetFileNameWithoutExtension(path);


	public static bool FolderExists (string path) => Directory.Exists(path);


	public static bool FileExists (string path) => !string.IsNullOrEmpty(path) && File.Exists(path);


}