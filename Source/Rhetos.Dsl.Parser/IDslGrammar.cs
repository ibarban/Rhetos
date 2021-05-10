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

using Rhetos.Utilities;

namespace Rhetos.Dsl
{
    public interface IDslGrammar
    {
        /// <summary>
        /// Semantic Versioning 2.0.0.
        /// </summary>
        string Version { get; }

        ConceptType[] ConceptTypes { get; }

        /// <summary>
        /// Value is initially configured from BuildOptions class.
        /// It is persisted as a part of <see cref="IDslGrammar"/> to be available to the external language server.
        /// </summary>
        ExcessDotInKey ExcessDotInKey { get; }

        /// <summary>
        /// Value is initially configured from DatabaseSettings class.
        /// It is persisted as a part of <see cref="IDslGrammar"/> to be available to the external language server.
        /// </summary>
        string DatabaseLanguage { get; }
    }
}