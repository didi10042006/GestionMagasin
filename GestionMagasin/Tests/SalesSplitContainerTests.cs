using System.Runtime.ExceptionServices;
using System.Drawing;
using System.Windows.Forms;
using GestionMagasin.Data;
using GestionMagasin.Forms;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class SalesSplitContainerTests
{
    [Fact]
    public void SalesFormInitializesSplitContainersBeforeRealWindowLayout()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GestionMagasin-SplitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "split-test.db");
        DatabaseInitializer.Initialize(databasePath);
        Exception? failure = null;
        using var finished = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SalesForm(new DatabaseSession(DatabaseMode.Reelle, databasePath), null);
                form.ClientSize = new Size(940, 620);
                form.CreateControl();
                _ = form.Handle;
                form.PerformLayout();
                Application.DoEvents();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                finished.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Sales form splitter regression test"
        };

        try
        {
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(20)), "Le formulaire de vente ne s'est pas initialisé à temps.");
            thread.Join();
            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
