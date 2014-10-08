﻿//==================================================================//
/*
    AtomicParsley - parsley.cpp

    AtomicParsley is GPL software; you can freely distribute,
    redistribute, modify & use under the terms of the GNU General
    Public License; either version 2 or its successor.

    AtomicParsley is distributed under the GPL "AS IS", without
    any warranty; without the implied warranty of merchantability
    or fitness for either an expressed or implied particular purpose.

    Please see the included GNU General Public License (GPL) for
    your rights and further details; see the file COPYING. If you
    cannot, write to the Free Software Foundation, 59 Temple Place
    Suite 330, Boston, MA 02111-1307, USA.  Or www.fsf.org

    Copyright ©2005-2007 puck_lock
    with contributions from others; see the CREDITS file

    ----------------------
    Code Contributions by:

    * Mike Brancato - Debian patches & build support
    * Lowell Stewart - null-termination bugfix for Apple compliance
    * Brian Story - native Win32 patches; memset/framing/leaks fixes
    ----------------------
    SVN revision information:
      $Revision$
                                                                   */
//==================================================================//
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;
using FRAFV.Binary.Serialization;

namespace MP4
{
	public sealed class Container
	{
		private static readonly ILog log = Logger.GetLogger(typeof(Container));
		internal const string XMLNS = "urn:schemas-mp4ra-org:container";

		[XmlIgnore]
		public ICollection<AtomicInfo> Atoms { get; private set; }

		[XmlElement("FileType", typeof(ISOMediaBoxes.FileTypeBox))]
		[XmlElement("JPEG2000", typeof(ISOMediaBoxes.JPEG2000Atom))]
		[XmlElement("Movie", typeof(ISOMediaBoxes.MovieBox))]
		[XmlElement("MediaData", typeof(ISOMediaBoxes.MediaDataBox))]
		[XmlElement(typeof(ISOMediaBoxes.ProgressiveDownloadBox))]
		[XmlElement("MovieFragment", typeof(ISOMediaBoxes.MovieFragmentBox))]
		//[XmlElement(typeof(mfra))]
		[XmlElement("Meta", typeof(ISOMediaBoxes.MetaBox))]
		[XmlElement("FreeSpace", typeof(ISOMediaBoxes.FreeSpaceBox))]
		[XmlElement("UUID", typeof(ISOMediaBoxes.UUIDBox))]
		[XmlElement(typeof(ISOMediaBoxes.ESDBox))]
		//[XmlElement(typeof(data))]
		[XmlElement(typeof(ISOMediaBoxes.UnknownBox))]
		[XmlElement(typeof(ISOMediaBoxes.UnknownParentBox))]
		public AtomicInfo[] AtomsSerializer
		{
			get
			{
				int k = 0;
				var res = new AtomicInfo[this.Atoms.Count];
				foreach (var atom in this.Atoms)
				{
					//atom.xmlOpt = this.xmlOpt;
					res[k++] = atom;
				}
				return res.ToArray();
			}
			set { this.Atoms = new List<AtomicInfo>(value); }
		}

		internal static TBox[] BoxListSerialize<TBox>(ICollection<TBox> list)
			where TBox: class
		{
			int k = 0;
			var res = new TBox[list.Count];
			foreach (TBox atom in list)
			{
				//atom.xmlOpt = this.xmlOpt;
				res[k++] = atom;
			}
			return res.ToArray();
		}

		internal static List<TBox> BoxListDeserialize<TBox>(TBox[] list)
			where TBox: class
		{
			return new List<TBox>(list);
		}

		public static readonly XmlSerializerNamespaces DefaultXMLNamespaces = new XmlSerializerNamespaces(
			new XmlQualifiedName[] { new XmlQualifiedName(String.Empty, XMLNS) });

		public Container()
		{
			this.Atoms = new List<AtomicInfo>();
		}

		#region Locating/Finding Atoms
		private int GetTrackCount()
		{
			return this.Atoms.Where(file => file is IBoxContainer).SelectMany(file => ((IBoxContainer)file).Boxes).
				Count(atom =>  atom.Name == "trak");
		}

		private AtomicInfo FindAtomInTrack(int track_num, AtomicCode search_atom)
		{
			int track_tally = 0;

			foreach (var atom in this.Atoms.Where(file => file is IBoxContainer).SelectMany(file => ((IBoxContainer)file).Boxes))
			{
				if (atom.Name == "trak")
				{
					track_tally += 1;
					if (track_num == track_tally)
					{
						//drill down into stsd
						return atom.FindAtom(search_atom);
					}
				}
			}
			return null;
		}

		#endregion

		#region File scanning & atom parsing
		int metadata_style = ApTypes.UNDEFINED_STYLE;
		bool psp_brand = false;
		long gapless_void_padding ; //possibly used in the context of gapless playback support by Apple

		private void IdentifyBrand(string brand)
		{
			switch (brand)
			{
			//what ISN'T supported
			case "qt  " : //this is listed at mp4ra, but there are features of the file that aren't supported (like the 4 NULL bytes after the last udta child atom
				throw new NotSupportedException("Quicktime movie files are not supported.");

			//
			//3GPP2 specification documents brands
			//

			case "3g2b" : //3GPP2 release A
				metadata_style = ApTypes.THIRD_GEN_PARTNER_VER2_REL_A;    //3GPP2 C.S0050-A_v1.0_060403, Annex A.2 lists differences between 3GPP & 3GPP2 - assets are not listed
				break;

			case "3g2a" : //                                //3GPP2 release 0
				metadata_style = ApTypes.THIRD_GEN_PARTNER_VER2;
				break;

			//
			//3GPP specification documents brands, not all are listed at mp4ra
			//

			case "3gp7" : //                                //Release 7 introduces ID32; though it doesn't list a iso bmffv2 compatible brand. Technically, ID32
			//could be used on older 3gp brands, but iso2 would have to be added to the compatible brand list.
			case "3gs7" : //                                //I don't feel the need to do that, since other things might have to be done. And I'm not looking into it.
			case "3gr7" :
			case "3ge7" :
			case "3gg7" :
				metadata_style = ApTypes.THIRD_GEN_PARTNER_VER1_REL7;
				break;

			case "3gp6" : //                                //3gp assets which were introducted by NTT DoCoMo to the Rel6 workgroup on January 16, 2003
			//with S4-030005.zip from http://www.3gpp.org/ftp/tsg_sa/WG4_CODEC/TSGS4_25/Docs/ (! albm, loci)
			case "3gr6" : //progressive
			case "3gs6" : //streaming
			case "3ge6" : //extended presentations (jpeg images)
			case "3gg6" : //general (not yet suitable; superset)
				metadata_style = ApTypes.THIRD_GEN_PARTNER_VER1_REL6;
				break;

			case "3gp4" : //                                //3gp assets (the full complement) are available: source clause is S5.5 of TS26.244 (Rel6.4 & later):
			case "3gp5" : //                                //"that the file conforms to the specification; it includes everything required by,
				metadata_style = ApTypes.THIRD_GEN_PARTNER;               //and nothing contrary to the specification (though there may be other material)"
				break;                                                    //it stands to reason that 3gp assets aren't contrary since 'udta' is defined by iso bmffv1

			//
			//other brands that are have compatible brands relating to 3GPP/3GPP2
			//

			case "kddi" : //                                //3GPP2 EZmovie (optionally restricted) media; these have a 3GPP2 compatible brand
				metadata_style = ApTypes.THIRD_GEN_PARTNER_VER2;
				break;
			case "mmp4" :
				metadata_style = ApTypes.THIRD_GEN_PARTNER;
				break;

			//
			//what IS supported for iTunes-style metadata
			//

			case "MSNV" : //(PSP) - this isn't actually listed at mp4ra, but since they are popular...
				metadata_style = ApTypes.ITUNES_STYLE;
				psp_brand = true;
				break;
			case "M4A " : //these are all listed at http://www.mp4ra.org/filetype.html as registered brands
			case "M4B " :
			case "M4P " :
			case "M4V " :
			case "M4VH" :
			case "M4VP" :
			case "mp42" :
			case "mp41" :
			case "isom" :
			case "iso2" :
			case "avc1" :


				metadata_style = ApTypes.ITUNES_STYLE;
				break;

			//
			//other brands that are derivatives of the ISO Base Media File Format
			//
			case "mjp2" :
			case "mj2s" :
				metadata_style = ApTypes.MOTIONJPEG2000;
				break;

			//other lesser unsupported brands; http://www.mp4ra.org/filetype.html like dv, mp21 & ... whatever mpeg7 brand is
			default:
				throw new NotSupportedException(String.Format("Unsupported MPEG-4 file brand found '{0}'", brand));
			}
			return;
		}

		/// <summary>
		/// ScanAtoms
		/// </summary>
		/// <param name="path">the complete path to the originating file to be tested</param>
		/// <param name="deepscan_REQ">controls whether we go into 'stsd' or just a superficial scan</param>
		/// <remarks>
		/// if the file has not yet been scanned (this gets called by nearly every cli
		/// option), then open the file and start scanning. Read in the first 12 bytes and
		/// see if bytes 4-8 are 'ftyp' as any modern MPEG-4 file will have 'ftyp' first.
		/// Accommodations are also in place for the jpeg2000 signature, but the sig.  must
		/// be followed by 'ftyp' and have an 'mjp2' or 'mj2s' brand. If it does, start
		/// scanning the rest of the file. An MPEG-4 file is logically organized into
		/// discrete hierarchies called "atoms" or "boxes". Each atom is at minimum 8 bytes
		/// long. Bytes 1-4 make an unsigned 32-bit integer that denotes how long this atom
		/// is (ie: 8 would mean this atom is 8 bytes long).  The next 4 bytes (bytes 5-8)
		/// make the atom name. If the atom presents longer than 8 bytes, then that
		/// supplemental data would be what the atom carries. Atoms are broadly separated
		/// into 2 categories: parents & children (or container & leaf).  Typically, a
		/// parent can hold other atoms, but not data; a child can hold data but not other
		/// atoms. This 'rule' is broken sometimes (the atoms listed as DUAL_STATE_ATOM),
		/// but largely holds.
		/// 
		/// Each atom is read in as 8 bytes. The atom name is extracted, and using the last
		/// known container (either FILE_LEVEL or an actual atom name), the new atom's
		/// hierarchy is found based on its length & position. Using its containing atom,
		/// the KnownAtoms table is searched to locate the properties of that atom (parent/
		/// child, versioned/simple), and jumping around in the file is based off that
		/// known atom's type. Atoms that fall into a hybrid category (DUAL_STATE_ATOMs)
		/// are explicitly handled. If an atom is known to be versioned, the version-and-
		/// flags attribute is read. If an atom is listed as having a language attribute,
		/// it is read to support multiple languages (as most 3GP assets do).
		/// </remarks>
		private void ScanAtoms(Stream file)
		{
			bool jpeg2000signature = false;

			var reader = new BinReader(file, false);

			long boxStart = reader.BaseStream.Position;
			long boxSize = reader.ReadUInt32();
			var atomid = reader.ReadAtomicCode();
			if (boxSize == 12L && atomid == ISOMediaBoxes.JPEG2000Atom.DefaultID)
			{
				jpeg2000signature = true;
			}
			if (atomid != ISOMediaBoxes.FileTypeBox.DefaultID && !jpeg2000signature)
			{
				throw new InvalidDataException("Bad mpeg4 file (ftyp atom missing or alignment error).");
			}
			var atom = AtomicInfo.ParseBox(reader, boxStart, (uint)boxSize, atomid);
			if (jpeg2000signature && ((ISOMediaBoxes.JPEG2000Atom)atom).Data.ToInt32(0, false) != ISOMediaBoxes.JPEG2000Atom.Signature)
			{
				throw new InvalidDataException("Bad jpeg2000 file (invalid header).");
			}

			if (!jpeg2000signature)
			{
				IdentifyBrand((string)((ISOMediaBoxes.FileTypeBox)atom).Brand);
			}

			this.Atoms.Add(atom);

			while (reader.BaseStream.Position < reader.BaseStream.Length)
			{

				atom = AtomicInfo.ParseBox(reader);
				this.Atoms.Add(atom);

				if (jpeg2000signature)
				{
					var ftyp = atom as ISOMediaBoxes.FileTypeBox;
					if (ftyp != null && boxSize >= 8L)
					{
						IdentifyBrand((string)ftyp.Brand);
					}
					else
					{
						throw new InvalidDataException("Expected ftyp atom missing."); //the atom right after the jpeg2000/mjpeg2000 signature is *supposed* to be 'ftyp'
					}
					jpeg2000signature = false;
				}
			}

			//if (brand == 0x69736F6D) { //'isom' test for amc files & its (?always present?) uuid 0x63706764A88C11D48197009027087703
			//    char EZ_movie_uuid[100];
			//    memset(EZ_movie_uuid, 0, sizeof(EZ_movie_uuid));
			//    memcpy(EZ_movie_uuid, "uuid=\x63\x70\x67\x64\xA8\x8C\x11\xD4\x81\x97\x00\x90\x27\x08\x77\x03", 21); //this is in an endian form, so it needs to be converted
			//    APar_endian_uuid_bin_str_conversion(EZ_movie_uuid+5);
			//    if ( APar_FindAtom(EZ_movie_uuid, false, EXTENDED_ATOM, 0, true) != NULL) {
			//        metadata_style = UNDEFINED_STYLE;
			//    }
			//}

			if (/*!deep_atom_scan &&*/ !this.Atoms.Any(box => box.Name == "moov"))
				throw new InvalidDataException("Bad mpeg4 file (no 'moov' atom).");
		}

		public static Container Create(Stream file)
		{
			var mp4 = new Container();
			mp4.ScanAtoms(file);
			return mp4;
		}
		#endregion
	}
}