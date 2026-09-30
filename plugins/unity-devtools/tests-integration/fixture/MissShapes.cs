// ReSharper disable UnusedType.Global UnusedMember.Global UnusedParameter.Global
// ReSharper disable NotAccessedField.Global NotAccessedField.Local MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Local ClassNeverInstantiated.Global

namespace TestFixture.Miss {
  // What a wrong name is answered with: every shape below exists to be listed, hinted at, or
  // suggested by a miss, and tests build their own instances inside the evaluated expression.
  public class Animal {
    public static int Population = 3;

    public int Legs = 4;

    private readonly int secret = 1;

    public int Reveal() => this.secret;

    public static string Kingdom => "animalia";

    public string Species => "animal";

    public string Nickname { get; set; } = "";

    public int this[int index] => index;

    public static string Classify(int legs) => legs > 2 ? "quadruped" : "biped";

    public string Speak() => "...";

    public string Speak(int times) => times.ToString();

    public T Echo<T>(T value) => value;
  }

  public sealed class Dog : Animal {
    public int Tricks;

    public string Breed => "mutt";

    public string Bark() => "woof";
  }

  public struct Paw {
    public int Claws;

    public bool IsLeft;
  }
}
