using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mcpserver.Domain.Contracts.Meters
{
    public sealed class MeterDto
    {
        public long? Id { get; set; }
        public string Name { get; set; } = "";


    }
}
