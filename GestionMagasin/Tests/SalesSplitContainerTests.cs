using System.Runtime.ExceptionServices;
using System.Drawing;
using System.Collections;
using System.Reflection;
using System.Windows.Forms;
using GestionMagasin.Data;
using GestionMagasin.Forms;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class SalesSplitContainerTests
{
    [Fact]
    public void DemoAddToCartButtonAddsSelectedProductAndRefreshesBoundCart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GestionMagasin-CartUiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "cart-test.db");
        DatabaseInitializer.Initialize(databasePath);
        var factory = new SqliteConnectionFactory(databasePath);
        DemoDataSeeder.SeedIfNeeded(factory);
        var demoCashier = DemoDataSeeder.EnsureDemoCashier(factory);
        Exception? failure = null;
        using var finished = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SalesForm(new DatabaseSession(DatabaseMode.Demonstration, databasePath), demoCashier)
                {
                    ClientSize = new Size(940, 620),
                    ShowInTaskbar = false,
                    Opacity = 0
                };
                form.Show();
                Application.DoEvents();

                var addButton = (Button)ReadPrivateField(form, "_addToCartButton");
                Assert.True(addButton.Enabled, "Le bouton d'ajout doit être disponible en mode démonstration.");
                addButton.PerformClick();
                Application.DoEvents();

                var cart = (ICollection)ReadPrivateField(form, "_cart");
                var cartGrid = (DataGridView)ReadPrivateField(form, "_cartGrid");
                var totalLabel = (Label)ReadPrivateField(form, "_totalLabel");
                var checkoutButton = (Button)ReadPrivateField(form, "_checkoutButton");
                var changeLabel = (Label)ReadPrivateField(form, "_changeLabel");
                Assert.Single((IEnumerable)cart);
                Assert.Single(cartGrid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow));
                Assert.NotEqual("Total : 0 FCFA", totalLabel.Text);
                Assert.True(checkoutButton.Enabled, "La validation reste cliquable afin d'afficher les erreurs métier de paiement.");
                Assert.Contains("Manque", changeLabel.Text);
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
            Name = "Sales form add-to-cart regression test"
        };

        try
        {
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(20)), "Le clic d'ajout au panier ne s'est pas terminé à temps.");
            thread.Join();
            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

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

    private static object ReadPrivateField(SalesForm form, string name) =>
        typeof(SalesForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
        ?? throw new InvalidOperationException($"Champ de formulaire introuvable : {name}");
}
