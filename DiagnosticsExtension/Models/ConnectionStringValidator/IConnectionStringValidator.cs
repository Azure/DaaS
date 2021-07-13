using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    interface IConnectionStringValidator
    {
        // verify provided string is a valid connection string that can be tested by the validator
        bool IsValid(string connStr);

        Task<ConnectionStringValidationResult> Validate(string connStr, string clientId = null);  // clientId used for Used Assigned Managed Identity

        string ProviderName { get; }

        ConnectionStringType Type { get; }


    }
}
