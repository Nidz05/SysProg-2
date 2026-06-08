# Sistemsko programiranje

Prvi projekat: https://github.com/pajonak1/sisprogelfak

# Zadatak 24:

Kreirati Web server koji klijentu omogućava pretragu knjiga korišćenjem Open Library API-a. Pretraga se može vršiti pomoću filtera koji se zadaju u okviru query-a. Spisak knjiga koje zadovoljavaju uslov se vraćaju kao odgovor klijentu. Svi zahtevi serveru se šalju preko browsera korišćenjem GET metode. Ukoliko navedene knjige ne postoje, prikazati grešku korisniku.

Način funkcionisanja Open Library API-a je moguće proučiti na sledećem linku: https://openlibrary.org/dev/docs/api/search

Primer poziva serveru: https://openlibrary.org/search.json?author=tolkien&sort=new
