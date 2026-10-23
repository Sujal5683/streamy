using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace EnterpriseRemote.Controller.UI.Utils;

/// <summary>
/// Thread-safe and OLE-resilient clipboard helper.
/// Dispatches calls to an STA thread if the calling thread is MTA,
/// retries on transient COM/OLE lock contention, and never throws unhandled exceptions.
/// </summary>
public static class SafeClipboard
{
    public static bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        bool success = false;
        void DoSet()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true, 4, 100);
                    success = true;
                    return;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(50);
                }
                catch
                {
                    Thread.Sleep(50);
                }
            }
        }

        try
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                DoSet();
            }
            else
            {
                var staThread = new Thread(DoSet);
                staThread.SetApartmentState(ApartmentState.STA);
                staThread.IsBackground = true;
                staThread.Start();
                staThread.Join(2000);
            }
        }
        catch
        {
            // Never throw to caller
        }

        return success;
    }

    public static string? TryGetText()
    {
        string? result = null;
        void DoGet()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        result = Clipboard.GetText();
                    }
                    return;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(50);
                }
                catch
                {
                    Thread.Sleep(50);
                }
            }
        }

        try
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                DoGet();
            }
            else
            {
                var staThread = new Thread(DoGet);
                staThread.SetApartmentState(ApartmentState.STA);
                staThread.IsBackground = true;
                staThread.Start();
                staThread.Join(2000);
            }
        }
        catch
        {
            // Never throw to caller
        }

        return result;
    }
}
