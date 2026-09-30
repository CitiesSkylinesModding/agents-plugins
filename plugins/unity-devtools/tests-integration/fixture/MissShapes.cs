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

  // The same simple name as Far.Twin, so a bare or misplaced "Twin" has two full names to offer.
  public sealed class Twin;

  // Holds "Twin" without being named it: a containing match, ranked behind the exact ones.
  public sealed class TwinTower;

  public sealed class Kennel {
    // A nested type, suggested under the dotted spelling the evaluator resolves.
    public sealed class Cage;
  }

  public struct Paw {
    public int Claws;

    public bool IsLeft;
  }
}

namespace TestFixture.Miss.Far {
  public sealed class Twin;
}
