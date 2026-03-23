namespace adrilight.Fakes
{
    class ModeManagerFake : Util.IModeManager
    {
        public Util.LightingMode ActiveMode => Util.LightingMode.ScreenCapture;
        public bool IsInhibited => false;
        public bool IsOutputActive => true;
        public void SetMode(Util.LightingMode mode) { }
        public void AddInhibitor(string source) { }
        public void RemoveInhibitor(string source) { }
    }
}
