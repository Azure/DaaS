using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DaaS.Sessions
{
    public class SessionId
    {
        public override int GetHashCode()
        {
            return (_id != null ? _id.GetHashCode() : 0);
        }

        private readonly string _id;

        public SessionId(string id)
        {
            _id = id;
        }

        private SessionId() { }

        public override string ToString()
        {
            return _id;
        }

        public override bool Equals(object obj)
        {
            SessionId otherSessionid = obj as SessionId;
            if (otherSessionid == null)
            {
                return false;
            }

            return this._id.Equals(otherSessionid._id, StringComparison.OrdinalIgnoreCase);
        }
    }
}
