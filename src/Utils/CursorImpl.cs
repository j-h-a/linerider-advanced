
//  Author:
//       Noah Ablaseau <nablaseau@hotmail.com>
//
//  Copyright (c) 2017 
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
using System;
using System.Windows.Forms;

namespace linerider.Utils
{
    public class CursorImpl : Gwen.Platform.Neutral.CursorImplementation
    {
        
        private GLWindow game;
        public CursorImpl(GLWindow game)
        {
            this.game = game;
        }
        public override void SetCursor(System.Windows.Forms.Cursor cursor)
        {
            if (cursor == Cursors.SizeNS)
            {
                game.Cursor = game.Cursors["size_ver"];
            }
            else if (cursor == Cursors.SizeWE)
            {
                game.Cursor = game.Cursors["size_hor"];
            }
            else if (cursor == Cursors.SizeNWSE)
            {
                game.Cursor = game.Cursors["size_nwse"];
            }
            else if (cursor == Cursors.SizeNESW)
            {
                game.Cursor = game.Cursors["size_nesw"];
            }
            else if (cursor == Cursors.Default)
            {
                game.Cursor = game.Cursors["default"];
            }
        }
    }
}