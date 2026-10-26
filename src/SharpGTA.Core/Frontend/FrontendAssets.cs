using System;
using System.Collections.Generic;
using System.IO;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Frontend
{
    /// <summary>Original III frontend resources; no window, GPU or native API is touched.</summary>
    public sealed class IIIFrontendAssets
    {
        private readonly TxdArchive menu;
        private readonly TxdArchive fonts;
        private readonly Dictionary<string, TxdImage> images =
            new Dictionary<string, TxdImage>(StringComparer.OrdinalIgnoreCase);

        private IIIFrontendAssets(TxdArchive menu, TxdArchive fonts)
        {
            this.menu = menu;
            this.fonts = fonts;
        }

        public static IIIFrontendAssets Load(IGameFileSystem files)
        {
            if (files == null)
                throw new ArgumentNullException("files");
            TxdArchive menu = TxdReader.Read(files.ReadAllBytes("MODELS/MENU.TXD", TxdReader.MaximumInputLength));
            TxdArchive fonts = TxdReader.Read(files.ReadAllBytes("MODELS/FONTS.TXD", TxdReader.MaximumInputLength));
            return new IIIFrontendAssets(menu, fonts);
        }

        public TxdImage GetImage(string name)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            TxdImage cached;
            if (images.TryGetValue(name, out cached))
                return cached;
            TxdTexture texture = menu.Find(name) ?? fonts.Find(name);
            if (texture == null)
                throw new KeyNotFoundException("III frontend texture was not found: " + name);
            TxdImage image = texture.DecodeLevel(0);
            images.Add(name, image);
            return image;
        }
    }

    public interface IFrontendText
    {
        string Get(string key);
    }

    public sealed class FrontendText : IFrontendText
    {
        private readonly IGameFileSystem files;
        private GxtArchive archive;

        public FrontendText(IGameFileSystem files, FrontendLanguage language)
        {
            if (files == null)
                throw new ArgumentNullException("files");
            this.files = files;
            SetLanguage(language);
        }

        public FrontendLanguage Language { get; private set; }

        public void SetLanguage(FrontendLanguage language)
        {
            string name;
            switch (language)
            {
                case FrontendLanguage.English: name = "AMERICAN.GXT"; break;
                case FrontendLanguage.French: name = "FRENCH.GXT"; break;
                case FrontendLanguage.German: name = "GERMAN.GXT"; break;
                case FrontendLanguage.Italian: name = "ITALIAN.GXT"; break;
                case FrontendLanguage.Spanish: name = "SPANISH.GXT"; break;
                default: throw new ArgumentOutOfRangeException("language");
            }
            using (Stream stream = files.OpenRead("TEXT/" + name))
                archive = GxtArchive.Read(stream);
            Language = language;
        }

        public string Get(string key)
        {
            if (key == null)
                throw new ArgumentNullException("key");
            string value;
            if (!archive.Main.TryGet(key, out value))
                throw new KeyNotFoundException("GXT key was not found in " + Language + ": " + key);
            return value;
        }
    }
}
