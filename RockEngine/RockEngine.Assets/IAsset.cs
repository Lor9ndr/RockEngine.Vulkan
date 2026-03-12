namespace RockEngine.Assets
{
    public interface IAsset: IPolymorphicSerializable
    {
        Guid ID { get; set; }

        string Name { get; set; }

        string Type { get; }

        DateTime Created { get; set; }

        DateTime Modified { get; set; }

        AssetPath Path { get; set; }
        HashSet<IAsset> Dependencies { get; }

        void UpdateModified();

        Task LoadDataAsync();

        void UnloadData();

        Type GetDataType();

        object GetData();

        void SetData(object data);

        void BeforeSaving();

        void AfterSaving();

        [System.Text.Json.Serialization.JsonIgnore]
        bool IsDataLoaded { get; }
    }

    public interface IAsset<TData> : IAsset where TData : class
    {
        [System.Text.Json.Serialization.JsonIgnore]
        TData? Data { get; }

    }

   
}
