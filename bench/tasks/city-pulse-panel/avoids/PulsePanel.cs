using Game.UI.InGame;

namespace CityPulse
{
    // GamePanel writes this type's full name as the payload's __Type, which is the key the
    // frontend registers the panel component under.
    public class PulsePanel : GamePanel
    {
        public override bool blocking => true;

        public override LayoutPosition position => LayoutPosition.Center;
    }
}
