namespace AssetStoreTools.Previews.Generators.Custom
{
    internal struct AudioChannelCoordinate
    {
        public int X { get; private set; }
        public int YBaseline { get; private set; }
        public int YAboveBaseline { get; private set; }
        public int YBelowBaseline { get; private set; }

        public AudioChannelCoordinate(int x, int yBaseline, int yAboveBaseline, int yBelowBaseline)
        {
            X = x;
            YBaseline = yBaseline;
            YAboveBaseline = yAboveBaseline;
            YBelowBaseline = yBelowBaseline;
        }
    }
}