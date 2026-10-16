using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// Sound preview: the format and length, and a button that plays the sound through its own player.
public sealed class SoundPreview : KindPreview
{
    public override string KindName => SoundKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var wav = (WavInfo)kind.Load(files, name);
        var seconds = wav.Data.Length / (double)(wav.SampleRate * wav.Channels * wav.BitsPerSample / 8);
        var box = new VBoxContainer();
        box.AddChild(
            new Label
            {
                Text =
                    $"PCM WAV, {wav.SampleRate} Hz, {(wav.Channels == 1 ? "mono" : "stereo")}, {wav.BitsPerSample}-bit, {seconds:0.00} s",
            }
        );
        var player = new AudioStreamPlayer { Stream = GodotDecoders.Wav(files.ReadAllBytes(name), name) };
        var play = new Button { Text = "Play" };
        play.Pressed += () =>
        {
            if (player.Playing)
                player.Stop();
            else
                player.Play();
            play.Text = player.Playing ? "Stop" : "Play";
        };
        player.Finished += () => play.Text = "Play";
        box.AddChild(player);
        box.AddChild(play);
        return box;
    }
}
