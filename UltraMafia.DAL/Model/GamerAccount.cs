using UlstraMafia.DAL;

namespace UltraMafia.DAL.Model
{
    public class GamerAccount : BaseEntity
    {
        public string IdExternal { get; set; }
        public string NickName { get; set; }
        public string PmChatId { get; set; }
    }
}