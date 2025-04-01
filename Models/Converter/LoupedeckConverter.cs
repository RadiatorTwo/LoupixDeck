using Newtonsoft.Json;

namespace LoupixDeck.Models.Converter
{
    internal class LoupedeckConverter : JsonConverter<LoupedeckLiveS>
    {
        private readonly IServiceProvider _provider;

        public LoupedeckConverter(IServiceProvider provider)
        {
            _provider = provider;
        }

        public override LoupedeckLiveS ReadJson(JsonReader reader, Type objectType, LoupedeckLiveS existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var instance = (LoupedeckLiveS)_provider.GetService(typeof(LoupedeckLiveS));
            serializer.Populate(reader, instance);
            return instance;
        }

        public override void WriteJson(JsonWriter writer, LoupedeckLiveS value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }
}
