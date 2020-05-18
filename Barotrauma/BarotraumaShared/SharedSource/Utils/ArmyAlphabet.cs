using System.Collections.Generic;

namespace Barotrauma
{
    public class ArmyAlphabetEntry
    {
        public char Character { get; set; }
        public string CodeWord { get; set; }

        public ArmyAlphabetEntry(char character, string codeWord)
        {
            Character = char.ToUpper(character);
            CodeWord = codeWord;
        }
    }


    public class ArmyAlphabetStorage
    {
        public List<ArmyAlphabetEntry> ArmyAlphabetList = new List<ArmyAlphabetEntry>();
        public Dictionary<char, ArmyAlphabetEntry> ArmyAlphabetLookup = new Dictionary<char, ArmyAlphabetEntry>();

        public ArmyAlphabetStorage()
        {
            CreateAlphabetEntries();
        }

        void CreateAlphabetEntries()
        {
            AddAlphabetEntry('A', "Alpha");
            AddAlphabetEntry('B', "Bravo");
            AddAlphabetEntry('C', "Charlie");
            AddAlphabetEntry('D', "Delta");
            AddAlphabetEntry('E', "Echo");
            AddAlphabetEntry('F', "Foxtrot");
            AddAlphabetEntry('G', "Golf");
            AddAlphabetEntry('H', "Hotel");
            AddAlphabetEntry('I', "India");
            AddAlphabetEntry('J', "Juliet");
            AddAlphabetEntry('K', "Kilo");
            AddAlphabetEntry('L', "Lima");
            AddAlphabetEntry('M', "Mike");
            AddAlphabetEntry('N', "November");
            AddAlphabetEntry('O', "Oscar");
            AddAlphabetEntry('P', "Papa");
            AddAlphabetEntry('Q', "Quebec");
            AddAlphabetEntry('R', "Romeo");
            AddAlphabetEntry('S', "Sierra");
            AddAlphabetEntry('T', "Tango");
            AddAlphabetEntry('U', "Uniform");
            AddAlphabetEntry('V', "Victor");
            AddAlphabetEntry('W', "Whiskey");
            AddAlphabetEntry('X', "X-Ray");
            AddAlphabetEntry('Y', "Yankee");
            AddAlphabetEntry('Z', "Zulu");
        }

        void AddAlphabetEntry(char c, string codeword)
        {
            ArmyAlphabetEntry entry = new ArmyAlphabetEntry(c, codeword);
            ArmyAlphabetList.Add(entry);
            ArmyAlphabetLookup.Add(entry.Character, entry);
        }
    }

    public class ArmyAlphabet
    {
        private static ArmyAlphabetStorage ms_storageInstance = new ArmyAlphabetStorage();

        public static string GetArmyAlphabetEntry(char character)
        {
            ArmyAlphabetEntry result;
            if (ms_storageInstance.ArmyAlphabetLookup.TryGetValue(character, out result))
                return result.CodeWord;

            return null;
        }

        public static string GetArmyAlphabetEntry(uint index)
        {
            if (ms_storageInstance.ArmyAlphabetList.Count > index)
                return ms_storageInstance.ArmyAlphabetList[(int)index].CodeWord;

            return null;
        }
    }
}