﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using Rhetos.Extensibility;
using Rhetos.Dsl.DefaultConcepts;
using Rhetos.Dsl;
using Rhetos.Utilities;

namespace Rhetos.DatabaseGenerator.DefaultConcepts
{
    [Export(typeof(IConceptDatabaseDefinition))]
    [ExportMetadata(MefProvider.Implements, typeof(BinaryPropertyInfo))]
    public class BinaryPropertyDatabaseDefinition : IConceptDatabaseDefinition
    {
        ConceptMetadata _conceptMetadata;
        ISqlUtility _sqlUtility;

        public BinaryPropertyDatabaseDefinition(ConceptMetadata conceptMetadata, ISqlUtility sqlUtility)
        {
            _conceptMetadata = conceptMetadata;
            _sqlUtility = sqlUtility;
        }

        public string CreateDatabaseStructure(IConceptInfo conceptInfo)
        {
            var info = (BinaryPropertyInfo)conceptInfo;

            PropertyDatabaseDefinition.RegisterColumnMetadata(_conceptMetadata, info, _sqlUtility.Identifier(info.Name), Sql.Get("BinaryPropertyDatabaseDefinition_DataType"));
            if (info.DataStructure is EntityInfo)
                return PropertyDatabaseDefinition.AddColumn(_conceptMetadata, info);
            
            return "";
        }

        public string RemoveDatabaseStructure(IConceptInfo conceptInfo)
        {
            var info = (BinaryPropertyInfo)conceptInfo;
            
            if (info.DataStructure is EntityInfo)
                return PropertyDatabaseDefinition.RemoveColumn(info, _sqlUtility.Identifier(info.Name));
            
            return "";
        }
    }
}
