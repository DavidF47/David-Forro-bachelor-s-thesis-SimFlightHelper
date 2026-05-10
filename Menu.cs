using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Thesis_testing_1
{
    public partial class Menu : Form
    {
        public Menu()
        {
            InitializeComponent();

            // A Menu_Load eseménykezelő újracsatolása / Reattaches the Menu_Load event handler
            this.Load -= Menu_Load;
            this.Load += Menu_Load;

            // Az ablak maximalizálásának letiltása / Disables maximizing the window
            this.MaximizeBox = false;

            // Fix méretű ablakkeret beállítása / Sets a fixed-size window border
            this.FormBorderStyle = FormBorderStyle.FixedSingle;

            // A METAR gomb alapértelmezett megjelenésének beállítása / Sets the default appearance of the METAR button
            METAR_button.BackColor = Color.White;
            METAR_button.FlatStyle = FlatStyle.Flat;
            METAR_button.FlatAppearance.BorderSize = 1;
            METAR_button.FlatAppearance.BorderColor = Color.Gray;

            // A TAF gomb alapértelmezett megjelenésének beállítása / Sets the default appearance of the TAF button
            TAF_button.BackColor = Color.White;
            TAF_button.FlatStyle = FlatStyle.Flat;
            TAF_button.FlatAppearance.BorderSize = 1;
            TAF_button.FlatAppearance.BorderColor = Color.Gray;

            // A CHARTS gomb alapértelmezett megjelenésének beállítása / Sets the default appearance of the CHARTS button
            CHARTS_button.BackColor = Color.White;
            CHARTS_button.FlatStyle = FlatStyle.Flat;
            CHARTS_button.FlatAppearance.BorderSize = 1;
            CHARTS_button.FlatAppearance.BorderColor = Color.Gray;

            // A Planner gomb alapértelmezett megjelenésének beállítása / Sets the default appearance of the Planner button
            Planner_button.BackColor = Color.White;
            Planner_button.FlatStyle = FlatStyle.Flat;
            Planner_button.FlatAppearance.BorderSize = 1;
            Planner_button.FlatAppearance.BorderColor = Color.Gray;
        }

        private void Menu_Load(object sender, EventArgs e)
        {
            // A worldcities.csv fájl elérési útjának összeállítása / Builds the path to the worldcities.csv file
            string cityPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "worldcities.csv");

            // A városadatok betöltése a CSV fájlból / Loads the city data from the CSV file
            CityData.LoadCities(cityPath);
        }

        private void METAR_button_Click(object sender, EventArgs e)
        {
            // Új METAR ablak létrehozása / Creates a new METAR window
            METAR metarForm = new METAR();

            // A METAR ablak megjelenítése / Displays the METAR window
            metarForm.Show();
        }

        private void TAF_button_Click(object sender, EventArgs e)
        {
            // Új TAF ablak létrehozása / Creates a new TAF window
            TAF tafForm = new TAF();

            // A TAF ablak megjelenítése / Displays the TAF window
            tafForm.Show();
        }

        private void CHARTS_button_Click_1(object sender, EventArgs e)
        {
            // Új charts ablak létrehozása / Creates a new charts window
            Chart_test chartsForm = new Chart_test();

            // A charts ablak megjelenítése / Displays the charts window
            chartsForm.Show();
        }

        private void Planner_button_Click(object sender, EventArgs e)
        {
            // Új Planner ablak létrehozása / Creates a new Planner window
            Planner plannerForm = new Planner();

            // A Planner ablak megjelenítése / Displays the Planner window
            plannerForm.Show();
        }

        private void METAR_button_MouseEnter(object sender, EventArgs e)
        {
            // A METAR gomb háttérszínének módosítása, amikor az egér fölé kerül / Changes the METAR button background color when the mouse enters
            METAR_button.BackColor = Color.LightGray;
        }

        private void METAR_button_MouseLeave(object sender, EventArgs e)
        {
            // A METAR gomb háttérszínének visszaállítása, amikor az egér elhagyja / Restores the METAR button background color when the mouse leaves
            METAR_button.BackColor = Color.White;
        }

        private void TAF_button_MouseEnter(object sender, EventArgs e)
        {
            // A TAF gomb háttérszínének módosítása, amikor az egér fölé kerül / Changes the TAF button background color when the mouse enters
            TAF_button.BackColor = Color.LightGray;
        }

        private void TAF_button_MouseLeave(object sender, EventArgs e)
        {
            // A TAF gomb háttérszínének visszaállítása, amikor az egér elhagyja / Restores the TAF button background color when the mouse leaves
            TAF_button.BackColor = Color.White;
        }

        private void CHARTS_button_MouseEnter(object sender, EventArgs e)
        {
            // A CHARTS gomb háttérszínének módosítása, amikor az egér fölé kerül / Changes the CHARTS button background color when the mouse enters
            CHARTS_button.BackColor = Color.LightGray;
        }

        private void CHARTS_button_MouseLeave(object sender, EventArgs e)
        {
            // A CHARTS gomb háttérszínének visszaállítása, amikor az egér elhagyja / Restores the CHARTS button background color when the mouse leaves
            CHARTS_button.BackColor = Color.White;
        }

        private void Other_button_MouseEnter(object sender, EventArgs e)
        {
            // A Planner gomb háttérszínének módosítása, amikor az egér fölé kerül / Changes the Planner button background color when the mouse enters
            Planner_button.BackColor = Color.LightGray;
        }

        private void Other_button_MouseLeave(object sender, EventArgs e)
        {
            // A Planner gomb háttérszínének visszaállítása, amikor az egér elhagyja / Restores the Planner button background color when the mouse leaves
            Planner_button.BackColor = Color.White;
        }
    }
}
