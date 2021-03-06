﻿using FSO.Files.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using deltaq.BsDiff;

namespace TSOVersionPatcher
{
    public class PatchApplier
    {
        public List<FileEntry> Patches;
        public List<FileEntry> Additions;
        public List<string> Deletions;

        private Stream Str;

        public PatchApplier(Stream str)
        {
            Str = str;
            using (var io = IoBuffer.FromStream(str, ByteOrder.LITTLE_ENDIAN))
            {
                var magic = io.ReadCString(4);
                if (magic != "TSOp")
                    throw new Exception("Not a TSO patch file!");
                var version = io.ReadInt32();

                var ips = io.ReadCString(4);
                if (ips != "IPS_")
                    throw new Exception("Invalid Patch Chunk!");

                var patchCount = io.ReadInt32();
                Patches = new List<FileEntry>();
                for (int i=0; i<patchCount; i++)
                {
                    Patches.Add(new FileEntry()
                    {
                        FileTarget = io.ReadVariableLengthPascalString().Replace('\\', '/'),
                        Length = io.ReadInt32(),
                        Offset = str.Position
                    });
                    str.Seek(Patches.Last().Length, SeekOrigin.Current);
                }


                var add = io.ReadCString(4);
                if (add != "ADD_")
                    throw new Exception("Invalid Addition Chunk!");

                var addCount = io.ReadInt32();
                Additions = new List<FileEntry>();
                for (int i = 0; i < addCount; i++)
                {
                    Additions.Add(new FileEntry()
                    {
                        FileTarget = io.ReadVariableLengthPascalString().Replace('\\', '/'),
                        Length = io.ReadInt32(),
                        Offset = str.Position
                    });
                    str.Seek(Additions.Last().Length, SeekOrigin.Current);
                }

                var del = io.ReadCString(4);
                if (del != "DEL_")
                    throw new Exception("Invalid Deletion Chunk!");

                var delCount = io.ReadInt32();
                Deletions = new List<string>();
                for (int i = 0; i < delCount; i++)
                {
                    Deletions.Add(io.ReadVariableLengthPascalString());
                }
            }
        }

        private void RecursiveDirectoryScan(string folder, HashSet<string> fileNames, string basePath)
        {
            var files = Directory.GetFiles(folder);
            foreach (var file in files)
            {
                fileNames.Add(Path.GetRelativePath(basePath, file));
            }

            var dirs = Directory.GetDirectories(folder);
            foreach (var dir in dirs)
            {
                RecursiveDirectoryScan(dir, fileNames, basePath);
            }
        }

        public void Apply(string source, string dest)
        {
            if (source != dest)
            {
                Console.WriteLine("Copying Unchanged Files...");
                //if our destination folder is different,
                //copy unchanged files first.
                var sourceFiles = new HashSet<string>();
                RecursiveDirectoryScan(source, sourceFiles, source);

                sourceFiles.ExceptWith(new HashSet<string>(Patches.Select(x => x.FileTarget)));
                sourceFiles.ExceptWith(new HashSet<string>(Deletions));

                foreach (var file in sourceFiles)
                {
                    var destP = Path.Combine(dest, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(destP));

                    File.Copy(Path.Combine(source, file), destP);
                }
                Console.WriteLine("Done!");
            }

            var reader = new BinaryReader(Str);
            foreach (var patch in Patches)
            {
                Console.WriteLine($"Patching {patch.FileTarget}...");
                var path = Path.Combine(source, patch.FileTarget);
                var dpath = Path.Combine(dest, patch.FileTarget);
                var data = File.ReadAllBytes(path);
                Directory.CreateDirectory(Path.GetDirectoryName(dpath));

                Str.Seek(patch.Offset, SeekOrigin.Begin);
                var patchd = reader.ReadBytes(patch.Length);
                BsPatch.Apply(data, patchd, File.Open(dpath, FileMode.Create, FileAccess.Write, FileShare.None));
            }
            Console.WriteLine("Patching Complete!");

            foreach (var add in Additions)
            {
                Console.WriteLine($"Adding File {add.FileTarget}...");
                var dpath = Path.Combine(dest, add.FileTarget);
                Directory.CreateDirectory(Path.GetDirectoryName(dpath));

                Str.Seek(add.Offset, SeekOrigin.Begin);
                var addData = reader.ReadBytes(add.Length);
                File.WriteAllBytes(dpath, addData);
            }

            foreach (var del in Deletions)
            {
                try
                {
                    File.Delete(Path.Combine(dest, del));
                    Console.WriteLine($"Deleted {del}.");
                }
                catch
                {
                    //file not found. not important - we wanted it deleted anyways.
                }
            }
        }
    }

    public class FileEntry
    {
        public string FileTarget;
        public long Offset;
        public int Length;
    }
}
