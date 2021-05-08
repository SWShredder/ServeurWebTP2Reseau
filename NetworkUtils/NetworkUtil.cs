/*
Nom : NetUtils
Auteur : PMC 2016-2018
Description : 

# Cette librairie fournie 
  - des méthodes d'extension pour les types TcpClient (et son stream) et UdpClient 
	- TcpClient encapsule un socket et permet de l'utiliser comme un stream ce qui confère des avantages au niveau de l'abstraction
	- UdpClient encapsule aussi un socket
	- Pour des nouveaux projets, je recommande de manipuler les abstractions TcpClient/NetworkStream et UdpClient directement,
	  donc sans manipuler les sockets directement.
  - des méthodes d'extension pour les socket fournies à titre patrimonial
  - des méthodes de fabrique pour des TcpClient et UdpClient
  - des validations et de la gestion d'exceptions inévitables en manipulant ces classes
  - des méthodes simplifiées pour certains protocoles text-based (HTTP, POP/SMTP) et la gestion des codes de réponses

# Certaine méthodes affichent des messages d'erreurs dans stderr, que vous pouvez rediriger ailleurs
	si vous ne voulez pas que ces messages s'affichent dans la console. C'est le comportement par défaut en mode console.
    En WinForms, vous ne les verrez pas nécessairement.


# Les méthodes d'envoi et de réception utilisent l'encodage ASCII par défaut. 
	Si vous envoyez des caractères non représentables, ils seront transformés en ?
	J'aurais pu privilégier ANSI (Encoding.Default) mais l'encodage et le décodage sur le serveur pourrait ne pas donner le même résultat.
	J'aurais pu utiliser alors UTF8 mais il y aurait eu un cout (overhead) de commmunication réseau. Négligeable, sauf à grand échelle.
	J'aurais aussi pu lancer une exception pour vour prévenir si vous "perdez" des caractères mais ca aussi aurait été couteux en traitement.
	
# J'ai choisi de ne lancer aucune exception dans la librairie.
	C'est discutable, mais ca va dans la même idée que votre utilitaire Console.
	Je capte donc certaines exception de la librairie. 
	Certaines sont normales et habituelles, je journalise donc et vous retourne un booléen.
	Certaines sont inhabituelles et je les relancent donc. Pour ces cas, la
	solution est probablement de corriger votre code de votre coté plutot que 
	de capter l'exception.

####	Réusinage pour ne plus exposer aucun Socket
	- serveur avec TcpListener
	- envoi/réception sur connexion TCP avec TcpClient toujours ou Stream
	- envoi/réception sur UDP avec UDPClient toujours.

*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Console;

namespace UtilCS.Network
{
	public static class NetworkUtils
	{
		public static string FinDeLigne = Environment.NewLine; // supporte seulement \n ou \r\n
		public static bool Verbose = false;

		//private static object baton = new object();

		/// <summary>
		/// Méthode tout-en-un qui prépare un socke en mode écoute (la file de clients pourra commencer)
		/// et qui attend après le premier client
		/// </summary>
		/// <param name="port"></param>
		/// <returns></returns>
		public static Socket ÉcouterTCPEtAccepter(int port)
		{
			Socket s = ÉcouterTCP(port); // crée le socket avec seulement le point de connexion local
			return s.Accept(); // retourne un socket avec les 2 points de connexion

		}

		/// <summary>
		/// TODO H19 recevoir le TcpListener et retourner TcpClient
		/// Ne fait pas grand chose mais je l'ai ajouté par souci 
		/// d'uniformité et d'extensibilité si jamais je veux ajouter des validations ou autre
		/// </summary>
		/// <param name="socket">Un socket maitre (en mode listen seulement, pas de remote endpoint)</param>
		/// <returns>Un socket client connecté</returns>
		public static Socket Accepter(this Socket socketMaitre)
		{
			Socket socketClient;
			try
			{
				socketClient = socketMaitre.Accept();
			}
			catch (SocketException ex)
			{
				Error.WriteLine("Erreur au moment d'accepter. Peu probable mais bon." +
					"Code erreur : " + ex.ErrorCode);
				throw ex;
			}
			return socketClient;
		}

		/// <summary>
		/// Prépare un socket en mode écoute (la file de client pourra commencer)
		/// mais on n'attend pas après le premier client (c'est Accept() qui fait ca)
		/// </summary>
		/// <param name="port"></param>
		/// <returns></returns>
		public static Socket ÉcouterTCP(int port)
		{
			Socket s = new Socket(SocketType.Stream, ProtocolType.Tcp);
			s.Bind(new IPEndPoint(IPAddress.Any, 1234));
			s.Listen(5); // 5 est assez standard pour la file...normalement le serveur accepte les clients assez rapidement alors ils ne restent pas dans la file
						 // À noter que c'est la limite de la file des clients non acceptés. Il pourra y en avoir plus que ca au total.
			return s;
		}

		/// <summary>
		/// Méthode qui bloque jusqu'à ce qu'un des sockets recoive une communication (prêt)
		/// À noter que aucune information n'est "recue" des sockets, 
		/// elle fait seulement retourner la liste des sockets prêts.
		/// </summary>
		/// <param name="listeComplèteSockets"></param>
		/// <returns></returns>
		public static List<Socket> AttendreSocketsPrêts(this List<Socket> listeComplèteSockets)
		{
			List<Socket> listeSocketsPrêts = new List<Socket>(listeComplèteSockets);
			// C'est la méthode Select() qui fait la magie. 
			// Elle "interroge" les sockets sans bloque sur un en particulier.
			// Elle bloque donc sur l'ensemble mais on pourrait spécifier un timeout.
			Socket.Select(listeSocketsPrêts, null, null, -1); // on attend pour toujours, pas de timeout. J'assume que vous allez exécuter ce code dans un thread dédié.
			return listeSocketsPrêts;

		}
		/// <summary>
		/// TODO H19 adapter !socket
		/// Retourne le premier
		/// </summary>
		/// <param name="listeComplèteSockets"></param>
		/// <returns></returns>
		public static Socket AttendrePremierSocketPrêt(this List<Socket> listeComplèteSockets)
		{
			List<Socket> listeSocketsPrêts = new List<Socket>(listeComplèteSockets);
			// C'est la méthode Select() qui fait la magie. 
			// Elle "interroge" les sockets sans bloque sur un en particulier.
			// Elle bloque donc sur l'ensemble mais on pourrait spécifier un timeout.
			Socket.Select(listeSocketsPrêts, null, null, -1); // on attend pour toujours, pas de timeout. J'assume que vous allez exécuter ce code dans un thread dédié.
			return listeSocketsPrêts[0];

		}


		/// <summary>
		/// Cette méthode va créer un socket et tenter de le connecter au serveur spécifié.
		/// </summary>
		/// <param name="host"></param>
		/// <param name="portDistant"></param>
		/// <returns>null si la connexion a échoué, sinon le client lui-même</returns>
		public static TcpClient PréparerSocketTCPConnecté(string host, int portDistant)
		{
			TcpClient tcpClient = new TcpClient();

			if (tcpClient.ConnecterTCP(host, portDistant))
				return tcpClient;
			else
				return null;
		}

		/// <summary>
		/// Cette méthode va tenter de connecter un socket existant au serveur spécifié
		/// Cette méthode fait simplement l'appel à Connect mais offre un 
		/// booléen de retour en plus. BIG DEAL!!
		/// </summary>
		/// <param name="tcpClient">le socket à connecter (extension de TcpClient)</param>
		/// <param name="host"></param>
		/// <param name="portDistant"></param>
		/// <returns>faux si la connexion a échoué</returns>
		public static bool ConnecterTCP(this TcpClient tcpClient, string host, int portDistant)
		{
			try
			{
				if (tcpClient == null) throw new InvalidOperationException("Veuillez instancier votre TcpClient."); // corrigé 2018-05-08

				tcpClient.Connect(host, portDistant);
				return true;
			}
			catch (SocketException ex)
			{
				// Erreur 11001 host not found
				// Erreur 10061 est la plus fréquente et habituelle (connexion refusée sur même machine)
				// Erreur 10060 timeout (sur machine distante)
				// Dans ces cas, on retourne faux tout simplement
				if (ex.ErrorCode == 10060 ||
						ex.ErrorCode == 10061 ||
						ex.ErrorCode == 11001)
					return false;

				throw;

			}
		}

		/// <summary>
		/// UDP n'est jamais connecté. "Connecter" un socket UDP permet seulement de ne pas avoir à spécifier le destinataire
		/// à chaque envoi, ce qui n'est pas le cas ici.
		/// </summary>
		/// <returns>un socket prêt à être utiliser pour </returns>
		public static UdpClient PréparerSocketUDPNonConnectéPourEnvoiMultiple()
		{
			UdpClient udpClient = new UdpClient();
			return udpClient;
		}

		/// <summary>
		/// En étant connecté, on recoit et envoie toujours au même
		/// point de connexion
		/// </summary>
		/// <param name="hostname"></param>
		/// <param name="portDistant"></param>
		/// <returns></returns>
		public static UdpClient PréparerSocketUDPConnecté(string hostname, int portDistant)
		{
			UdpClient udpClient = new UdpClient();
			udpClient.ConnecterUDP(hostname, portDistant);
			return udpClient;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="portDistant"></param>
		/// <returns></returns>
		public static UdpClient PréparerSocketUDPConnectéPourDiffusion(int portDistant)
		{
			UdpClient udpClient = new UdpClient();

			udpClient.ConnecterUDPPourDiffusion(portDistant);
			return udpClient;
		}

		/// <summary>
		/// Solution de contournement. J'essaye d'éviter d'avoir à créer autant
		/// de socket de diffusion que d'interfaces réseau.
		/// https://stackoverflow.com/questions/1096142/broadcasting-udp-message-to-all-the-available-network-cards
		/// </summary>
		/// <param name="portDistant"></param>
		/// <returns></returns>
		//public static BroadcastClient PréparerDiffusionUniverselleTrafiqué(int portDistant)
		//{
		//	return new BroadcastClient(portDistant);

		//}

		/// <summary>
		/// Ne "connecte" pas vraiment comme avec TCP. 
		/// Le Connect de UDP permet simplement d'éviter d'avoir à spécifier 
		/// la destination à chaque Send ultérieur.
		/// </summary>
		/// <param name="udpClient">le client</param>
		/// <param name="portDistant">le port "remote", cette méthode ne permet pas de choisir le port sortant</param>
		/// <returns></returns>
		public static bool ConnecterUDPPourDiffusion(this UdpClient udpClient, int portDistant)
		{
			try
			{
				udpClient.Connect(new IPEndPoint(IPAddress.Broadcast, portDistant));
				Debug.WriteLine("L'adresse de diffusion {0} sera utilisée.", IPAddress.Broadcast);
				return true;
			}
			catch (SocketException)
			{
				Error.WriteLine("Impossible de préparer un socket pour diffusion vers le port {0}.", portDistant);
				return false;
			}
		}

		/// <summary>
		/// Ne "connecte" pas vraiment comme avec TCP. 
		/// Le Connect de UDP permet simplement d'éviter d'avoir à spécifier 
		/// la destination à chaque Send ultérieur.
		/// </summary>
		/// <param name="udpClient"></param>
		/// <param name="hostname"></param>
		/// <param name="portDistant">le port "remote", cette méthode ne permet pas de choisir le port sortant</param>
		/// <returns></returns>
		public static bool ConnecterUDP(this UdpClient udpClient, string hostname, int portDistant)
		{
			try
			{

				udpClient.Connect(hostname, portDistant);
				return true;
			}
			catch (SocketException ex)
			{
				Error.WriteLine("Erreur de connexion code : " + ex.ErrorCode);
				return false;
			}
		}

		/// <summary>
		/// Tente de démarrer l'écoute sur le premier port UDP
		/// disponible à partir du port de début fourni.
		/// </summary>
		/// <param name="portDépart">Port de départ, sera ajusté au port actuel</param>
		/// <param name="clientUDP">L'objet représentant la connexion</param>
		/// <returns>Le port actuel d'écoute ou -1 si impossible de démarrer l'écoute</returns>
		public static bool ÉcouterUDPPremierPortDisponible(int portDépart, out UdpClient clientUDP)
		{
			for (int i = 0; i < 10; ++i)
			{
				try
				{
					clientUDP = new UdpClient();
					IPEndPoint localEndpoint = new IPEndPoint(IPAddress.Any, portDépart + i);
					clientUDP.Client.Bind(localEndpoint);

					return true;
				}
				catch (SocketException)
				{
					WriteLine("Erreur de création du socket");
					clientUDP = null;

				}
			}
			clientUDP = null;
			return false;
		}

		/// <summary>
		/// Créé un socket UDP et l'initialise pour recevoir
		/// sur toutes les interfaces
		/// </summary>
		/// <param name="port"></param>
		/// <returns></returns>
		public static UdpClient PréparerSocketUDPPourRéception(int port)
		{
			UdpClient udpClient;
			if (!ÉcouterUDP(port, out udpClient))
				Error.WriteLine("Impossible de préparer l'écoute.");

			return udpClient; // peu importe si c'est null


		}


		/// <summary>
		/// Initialise un socket existant UDP pour recevoir
		/// sur toutes les interfaces
		/// </summary>
		/// <remarks>
		/// </remarks>
		/// <param name="port"></param>
		/// <param name="clientUDP"></param>
		/// <returns></returns>
		public static bool ÉcouterUDP(int port, out UdpClient clientUDP)
		{
			try
			{
				clientUDP = new UdpClient();
				IPEndPoint localEndpoint = new IPEndPoint(IPAddress.Any, port);
				clientUDP.Client.Bind(localEndpoint);
				return true;
			}
			catch (SocketException)
			{
				Error.WriteLine("Erreur de création du socket. Port déja en utilisation.");
				clientUDP = null;

			}
			return false;
		}

		/// <summary>
		/// ATTENTION, NE MARCHE PAS PARFAITEMENT, MÊME SI PLUSIEURS SOCKET ÉCOUTENT SUR LE MEME PORT UDP, UN SEUL RECOIT LE MESSAGE
		/// CA PEUT ÊTRE ACCEPTABLE DANS CERTAINS CAS, MAIS C'EST RAREMENT CE QU'ON VEUT.
		/// </summary>
		/// <param name="port"></param>
		/// <param name="clientUDP"></param>
		/// <returns></returns>
		public static bool ÉcouterUDPRéutilisable(int port, out UdpClient clientUDP)
		{
			try
			{
				clientUDP = new UdpClient();
				// La réutilisation du socket permettra de partir plusieurs clients NP qui écouteront tous sur le port UDP
				clientUDP.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				clientUDP.EnableBroadcast = true;
				IPEndPoint localEndpoint = new IPEndPoint(IPAddress.Any, port);

				clientUDP.Client.Bind(localEndpoint);

				return true;
			}
			catch (SocketException)
			{
				WriteLine("Erreur de création du socket");
				clientUDP = null;

			}
			return false;
		}

		public static bool EnvoyerEtObtenirRéponse(this TcpClient c, string message, out string réponse)
		{
			c.EnvoyerMessage(message);
			return c.RecevoirMessage(out réponse);

		}

		/// <summary>
		/// Envoie la requête (et ajoute la fin de ligne) et 
		/// recoit une ligne représentant la réponse.
		/// Pour les protocoles avec des réponses multilignes, considérez utiliser RecevoirJusquaCode()
		/// </summary>
		/// <param name="s"></param>
		/// <param name="requête"></param>
		/// <param name="réponse"></param>
		/// <returns></returns>
		public static bool EnvoyerRequeteEtObtenirRéponse(this Stream s, string requête, out string réponse)
		{
			s.EnvoyerMessage(requête);
			return s.RecevoirLigne(out réponse);
		}



		/// <summary>
		/// TODO pour h19 désuet, utiliser TcpClient ou Stream
		/// Permet d'envoyer un array de byte complètement.
		/// Utile seulement si on veut envoyez des données binaires possiblement non représentables avec les encodages disponibles ASCII, ANSI (Default) ou UTF8.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="données"></param>
		public static void EnvoyerMessage(this Socket s, byte[] données)
		{
			EnvoyerMessage(s, données, données.Length);
		}

		/// <summary>
		/// Permet d'envoyer partiellement un array de byte.
		/// Utile seulement si on veut envoyez des données binaires possiblement non représentables avec les encodages disponibles ASCII, ANSI (Default) ou UTF8.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="données">le buffer au complet</param>
		/// <param name="taille">le nb d'octets du buffer à envoyer </param>
		public static void EnvoyerMessage(this Socket s, byte[] données, int taille)
		{
			try
			{
				taille = s.Send(données, taille, SocketFlags.None);
			}
			catch (SocketException ex)
			{
				Error.WriteLine("Erreur d'envoi code : " + ex.ErrorCode);
				throw;
			}
		}

		public static void EnvoyerMessage(this Socket s, string message, bool ajouterFinLigne = true, string encodage = "ASCII")
		{
			if (ajouterFinLigne)
				message += FinDeLigne;


			AfficherEnvoi(message);

			byte[] données = Encoding.GetEncoding(encodage).GetBytes(message);

			EnvoyerMessage(s, données);

		}

		/// <summary>
		/// Redirige l'appel à tcpclient.GetStream()
		/// Non fonctionnera pas si vous êtes sur une connexion sécurisée.
		/// Dans ce cas, utilisez plutot votre propre stream Ssl.
		/// </summary>
		/// <param name=""></param>
		/// <param name="message"></param>
		/// <param name="ajouterFinLigne">fin de ligne Windows</param>
		public static void EnvoyerMessage(this TcpClient client, string message, bool ajouterFinLigne = true, string encodage = "ASCII")
		{
			//throw new InvalidOperationException("Utilisez la méthode sur votre stream directement");
			client.GetStream().EnvoyerMessage(message, ajouterFinLigne, encodage);
		}

		/// <summary>
		/// Pour TCP (ou SSL), on ajoute une fin de ligne puisque TCP est stream-orienté, rien ne nous
		/// garanti que le message sera reçu en un morceau sur le fil même si
		/// envoyé en un seul appel à Send à partir d'ici.
		/// </summary>
		/// <param name="s">le buffer interne du socket TCP</param>
		/// <param name="message"></param>
		/// <param name="ajouterFinLigne">mettre à false si votre message contient déja un \n</param> 
		//public static void EnvoyerMessage(this Stream s, string message, bool ajouterFinLigne = true, string encodage = "ASCII")
		//{
		//	EnvoyerMessage((s, message, ajouterFinLigne, encodage);
		//}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="s"></param>
		/// <param name="messageÀRecevoir"></param>
		/// <param name="messageÀEnvoyer"></param>
		/// <param name="ajouterFinLigne"></param>
		public static void EnvoyerMessageAprès(this Stream s, string messageÀRecevoir, string messageÀEnvoyer, bool ajouterFinLigne = true)
		{
			// recevoir jusqu'à
			s.RecevoirMessagesJusqua(out string messageRecu, messageÀRecevoir);
			s.EnvoyerMessage(messageÀEnvoyer, ajouterFinLigne);

		}



		public static void EnvoyerMessage(this Stream s, string message, bool ajouterFinLigne = true, string encodage = "ASCII")
		{
			if (ajouterFinLigne)
				message += FinDeLigne;


			byte[] données = Encoding.GetEncoding(encodage).GetBytes(message);
			try
			{
				s.Write(données, 0, données.Length);
				AfficherEnvoi(message);
			}
			catch (IOException ex)
			{
				SocketException socketEx = ex.InnerException as SocketException;
				if (socketEx != null && socketEx.NativeErrorCode == 10054)
					Error.WriteLine("Impossible d'envoyer sur ce socket car l'autre point de connexion a été déconnecté");
				else
					throw;
			}
		}

		/// <summary>
		/// Utile lorsque le destinaire a déjà été spécifié dans le Connect
		/// </summary>
		/// <param name="s">un client UDP déja connecté</param>
		/// <param name="message"></param>
		public static void EnvoyerMessage(this UdpClient s, string message, string encodage = "ASCII")
		{
			byte[] données = Encoding.GetEncoding(encodage).GetBytes(message);
			s.Send(données, données.Length);
			AfficherEnvoi(message);

		}



		/// <summary>
		/// Équivalent du SendTo
		/// 
		/// </summary>
		/// <param name="s">le client UDP non connecté</param>
		/// <param name="message"></param>
		/// <param name="destinataire">le destinataire</param>
		public static void EnvoyerMessage(this UdpClient s, string message, IPEndPoint destinataire, string encodage = "ASCII")
		{
			EnvoyerMessage(s, message, destinataire, encodage);
		}

		public static void EnvoyerMessage(this UdpClient s, string message, IPEndPoint destinataire, Encoding encoding)
		{
			byte[] données = encoding.GetBytes(message);
			s.Send(données, données.Length, destinataire);
			AfficherEnvoi(message);
		}

		/// <summary>
		/// TODO : surcharger avec le choix de l'encoding en paramètre
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <returns>faux si erreur de réception ou si l'autre s'est déconnecté anormalement</returns>
		public static bool RecevoirMessage(this Socket s, out string message)
		{
			try
			{
				byte[] données = new byte[256];
				int taille = s.Receive(données);
				message = Encoding.ASCII.GetString(données, 0, taille);
				return taille > 0; // corrigé 2018-03-06 au lieu de return true toujours
			}
			catch (SocketException ex)
			{
				// Il est normal d'avoir une erreur 10054 connection reset quand l'autre détruit la connexion sans appeler un close en bonne et dû forme
				if (ex.ErrorCode != 10054) // For more information about socket error codes, see the Windows Sockets version 2 API error code documentation in MSDN.
					Error.WriteLine("Erreur anormale de réception code : " + ex.ErrorCode);
				message = "";
				return false;
			}
		}

		/// <summary>
		/// Utilisez seulement cette méthode pour recevoir des données non textuelles
		/// ou pour une chaine que vous ne connaissez pas nécessairement l'encodage.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="données">un buffer déja alloué</param>
		/// <param name="taille">le nb d'octets écrits dans le buffer</param>
		/// <returns></returns>
		public static bool RecevoirMessage(this Socket s, ref byte[] données, out int taille)
		{
			try
			{
				taille = s.Receive(données);
				return taille > 0; // corrigé 2018-03-06 au lieu de return true toujours
			}
			catch (SocketException ex)
			{
				// Il est normal d'avoir une erreur 10054 connection reset quand l'autre détruit la connexion sans appeler un close en bonne et dû forme
				if (ex.ErrorCode != 10054) // For more information about socket error codes, see the Windows Sockets version 2 API error code documentation in MSDN.
					Error.WriteLine("Erreur anormale de réception code : " + ex.ErrorCode);
				données = null;
				taille = 0;
				return false;
			}
		}
		/// <summary>
		/// 
		/// </summary>
		/// <param name="tcpClient"></param>
		public static void FermerSocketEtLibérer(this TcpClient tcpClient)
		{
			tcpClient.GetStream().Close();
			tcpClient.Close();

		}
		/// <summary>
		/// 
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <returns></returns>
		public static bool FermerAvecAffichage(this Stream s, string message)
		{
			s.Close();
			WriteLine(message);
			return true;
		}

		/// <summary>
		/// Affichage dans stdout
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <param name="message"></param>
		/// <returns></returns>
		//public static bool FermerAvecAffichage(this TcpClient tcpClient, string message)
		//{
		//  return tcpClient.FermerAvecAffichage(message);
		//}

		/// <summary>
		/// TODO pour remplacer RecevoirJusquaPoint et RecevoirLigne
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <param name="code">Pourrait être "\r\n.\r\n" ou simplement "\n"</param>
		/// <returns></returns>
		public static bool RecevoirLignesJusquaLigneCodée(this Stream s, out string message, string ligneCodée)
		{
			message = "";
			string ligne;
			while (s.RecevoirLigne(out ligne) && ligne != ligneCodée)
			{
				message += ligne;
			}
			return true;
		}

		public static bool ConsommerJusquaLigneSeTerminantPar(this Stream s, string suffixeLigne, bool finDeLigneWindows = true)
		{
			RecevoirJusquaLigneSeTerminantPar(s, out string message, suffixeLigne);
			return true;
		}

		public static bool RecevoirJusquaLigneSeTerminantPar(this Stream s, out string message, string suffixeLigne)
		{
			message = "";
			string ligne;
			while (s.RecevoirLigne(out ligne) && !ligne.EndsWith(suffixeLigne)) ;

			message = ligne;

			return true;
		}

		public static bool RecevoirMessagesJusqua(this TcpClient tcpClient, out string message, string messageÀRecevoir)
		{
			return tcpClient.GetStream().RecevoirMessagesJusqua(out message, messageÀRecevoir);
		}
		/// <summary>
		/// Recoit des messages TCP jusqu'à ce que le message complet se termine par la valeur spécifiée
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <param name="messageÀRecevoir"></param>
		/// <returns></returns>
		public static bool RecevoirMessagesJusqua(this Stream s, out string message, string messageÀRecevoir)
		{
			message = "";
			string portionDuMessage;
			while (true)
			{
				if (!s.RecevoirMessage(out portionDuMessage)) return false;

				message += portionDuMessage;
				/***/
				if (portionDuMessage.EndsWith(messageÀRecevoir)) break;
				/***/

			}
			return true;
		}

		/// <summary>
		/// Recoit tout (ligne ou non) jusqu'au point (utile pour le protocole POP3 et autre...)
		/// Le point est exclu.
		/// 
		/// Bug connu : pourrait ne pas détecter le .\r\n si d'autre contenu a été 
		/// envoyé d'avance par le serveur. Par exemple si on envoi LIST et une autre commande
		/// avant de faire le premier Recevoir(). 
		/// 
		/// De toute facon, vous devriez normalement récupérer les réponses
		/// du serveur au fur et à mesure et ce problème n'arrivera pas.
		/// 
		/// Pour corriger complètement, il faudrait y aller caractère par
		/// caractère comme la fonction RecevoirLigne()
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <returns></returns>
		public static bool RecevoirJusquaPoint(this Stream s, out string message)
		{
			message = "";
			string portionDuMessage;
			while (true)
			{
				if (!s.RecevoirMessage(out portionDuMessage)) return false;

				message += portionDuMessage;
				if (portionDuMessage.EndsWith(".\r\n"))
				{
					// TODO : enlever le .\r\n
					break;
				}

			}
			return true;
		}

		/// <summary>
		/// Redirige l'appel à tcpclient.GetStream()
		/// Non fonctionnera pas si vous êtes sur une connexion sécurisée.
		/// Dans ce cas, utilisez plutot votre propre stream Ssl.
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <param name="message"></param>
		/// <returns>vrai si quelque chose a été reçu</returns>
		public static bool RecevoirLigne(this TcpClient tcpClient, out string message, bool finLigneWindows = true, bool afficher = true)
		{
			return tcpClient.GetStream().RecevoirLigne(out message);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <param name="finLigneWindows"></param>
		/// <returns></returns>
		public static bool IgnorerLigne(this TcpClient tcpClient, bool finLigneWindows = true)
		{
			return tcpClient.GetStream().RecevoirLigne(out _);
		}

		public static bool IgnorerLignes(this TcpClient tcpClient, int nbLignes, bool finLigneWindows = true)
		{
			if (nbLignes < 1) throw new ArgumentException("Veuillez spécifier le nombre de lignes");

			for (int i = 0; i < nbLignes; i++)
			{
				tcpClient.IgnorerLigne(finLigneWindows);
			}

			return true;

		}


		/// <summary>
		/// Pour TCP seulement
		/// Va attendre de recevoir la séquence \r\n et bloquer (attendre) au besoin.
		/// </summary>
		/// <returns >Vrai si quelque chose a été reçu, faux si déconnecté ou si rien recu</returns>
		/// <remarks>Vous pourriez recevoir vrai et être quand même déconnecté. Il y a la propriété Connected du socket qui sera mise à jour</remarks>
		public static bool RecevoirLigne(this Stream s, out string message)
		{
			try
			{
				bool finDeLigneWindows = FinDeLigne == Environment.NewLine;

				List<Byte> données = new List<byte>();
				int c;
				for (; ; )
				{
					c = s.ReadByte();
					if (c == -1) break;
					if (finDeLigneWindows && c == (int)'\r' && s.ReadByte() == (int)'\n') break;
					else if (!finDeLigneWindows && c == '\n') break;

					données.Add((byte)c);
				}

				message = Encoding.ASCII.GetString(données.ToArray(), 0, données.Count);

				AfficherRéception(message);


				return données.Count > 0; // corrigé 2016-05-19 au lieu de return true
			}
			catch (Exception ex)
			{
				Error.WriteLine(ex.ToString());

			}
			message = null;
			return false;
		}

		/// <summary>
		/// Redirige l'appel à tcpclient.GetStream()
		/// Non fonctionnera pas si vous êtes sur une connexion sécurisée.
		/// Dans ce cas, utilisez plutot votre propre stream Ssl.
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <param name="message"></param>
		/// <returns>vrai si quelque chose a été reçu</returns>
		public static bool RecevoirMessage(this TcpClient tcpClient, out string message)
		{
			bool res = tcpClient.GetStream().RecevoirMessage(out message);
			AfficherRéception(message);

			return res;
		}

		/// <summary>
		/// J'ai généralisé cette fonction de NetworkStream à Stream pour supporter SslStream
		/// Si vous implémentez un protocole texte basé sur des lignes entières,
		/// considérez utiliser RecevoirLigne() ou RecevoirJusquaCode()
		/// Le message sera de taille maximale de 256 octets ce qui n'est
		/// pas un problème en soi puisqu'on peut appeller cette méthode en boucle.
		/// Considérez utiliser RecevoirTout() si vous voulez recevoir tout jusqu'à la fin de la connexion.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <returns>vrai si quelque chose a été reçu</returns>
		public static bool RecevoirMessage(this Stream s, out string message)
		{
			return RecevoirMessage(s, out message, Encoding.ASCII);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <param name="encoding"></param>
		/// <returns></returns>
		public static bool RecevoirMessage(this Stream s, out string message, Encoding encoding)
		{
			try
			{
				byte[] données = new byte[256];
				int taille = s.Read(données, 0, données.Length);
				message = encoding.GetString(données, 0, taille);
				AfficherRéception(message);
				return taille > 0; // corrigé 2016-05-19 au lieu de return true toujours
			}
			catch (IOException ex)
			{
				// https://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k%28System.Net.Sockets.NetworkStream.Read%29;k%28DevLang-csharp%29&rd=true
				Error.WriteLine("Erreur de réception, la connexion a été fermée." + ex.Message);
			}
			catch (ObjectDisposedException ex)
			{
				Error.WriteLine("Erreur de réception, le stream est fermé ou erreur de lecture sur le réseau." + ex.Message);
			}
			message = null;
			return false;
		}

		private static void AfficherRéception(string message)
		{
			if (Verbose)
				Console.WriteLine("<<<< " + message);
			//AfficherInfo("<<<< " + message);
		}

		private static void AfficherEnvoi(string message)
		{
			if (Verbose)
				Console.WriteLine(">>>>> " + message);
			//AfficherInfo(">>>>> " + message);
		}

		public static bool RecevoirTout(this TcpClient s, out string messageComplet)
		{
			return RecevoirTout(s.GetStream(), out messageComplet);
		}

		/// <summary>
		/// Si vous appellez cette méthode, une réception sera faite
		/// jusqu'à temps que la connexion soit fermée par l'autre point de connexion et ce,
		/// peu importe si c'est une déconnexion normale ou anormale (10054).
		/// </summary>
		/// <param name="s"></param>
		/// <param name="messageComplet"></param>
		/// <remarks>Ne bloque pas si appellé sur une connexion fermé</remarks>
		/// <returns>vrai indique que quelque chose à été reçu</returns>
		public static bool RecevoirTout(this Stream s, out string messageComplet)
		{
			// Pseudo détection pour voir si la connexion est fermée puisque pas accès au TcpClient d'ici
			// Si CanRead est toujours à true, la réception retournera instantanément et ne bloquera pas.
			// 
			if (!s.CanRead) throw new InvalidOperationException("Ne pas appeler sur une connexion déja fermée.");

			string message;
			messageComplet = "";
			bool recuQuelqueChose = false;
			while (s.RecevoirMessage(out message))
			{
				recuQuelqueChose = true;
				messageComplet += message;
			}
			return recuQuelqueChose;
		}

		/// <summary>
		/// L'idée est de recevoir tout sur une connexion TCP
		/// qui n'est pas fermée après par le serveur (keep-alive).
		/// L'idéal serait plutot de recevoir jusqu'à un certain code
		/// comme une fin de ligne, une ligne spéciale vide ou contenant
		/// seulement un point (POP3) ou simplement de recevoir
		/// la quantité prévue. En HTTP le serveur vous donne le content-length
		/// alors vous pouvez vous en servir.
		/// Sinon, si vous pouvez vous servir de cette méthode (moins efficace)
		/// qui regarde dans le flux à savoir s'il y a quelque chose
		/// qui n'a pas été recu (lu).
		/// </summary>
		/// <param name="s"></param>
		/// <param name="messageComplet"></param>
		/// <returns></returns>
		public static bool RecevoirToutKeepAlive(this TcpClient s, out string messageComplet)
		{
			throw new NotImplementedException("TODO");
			//while (s.Connected && s.Available)
		}


		/// <summary>
		/// Cette fonction est un "wrapper" de la méthode Receive().
		/// Va attendre jusqu'à ce qu'un paquet soit reçu.
		/// </summary>
		/// <param name="s">le socket sur lequel il faut recevoir</param>
		/// <param name="endPoint">stockera le point de connexion de l'émetteur (IP et port)</param>
		/// <param name="message">le contenu du paquet recu encodé en string</param>
		/// <returns>retourne faux si la réception à échouée</returns>
		public static bool RecevoirMessage(this UdpClient s, ref IPEndPoint endPoint, out string message)
		{
			try
			{
				byte[] données = s.Receive(ref endPoint);
				message = Encoding.ASCII.GetString(données);
				AfficherRéception(message);
				return true;
			}
			catch (SocketException ex)
			{
				// Probablement un code 10054. Possible surtout si c'est en boucle locale (i.e si l'autre point de connexion est sur la même machine)
				// ou un 10060 (TimeOut) si l'UdpClient a un receiveTimeout de spécifié (ce qui n'est pas le cas par défaut)
				Error.WriteLine("Erreur de socket au moment de la réception UDP.\n" +
																"Code Winsock : " + ex.SocketErrorCode);
			}
			catch (ObjectDisposedException ex)
			{
				Error.WriteLine("Erreur de socket au moment de la réception,\n" +
																"le socket a été fermé.\n" +
																ex.Message);
			}
			message = null;
			return false;
		}



		/// <summary>
		/// Cette méthode permet de fournir un délai et d'attendre
		/// 
		/// On aurait pu aussi utiliser le modèle async-await
		/// ou faire du polling sur UdpClient.Available
		/// </summary>
		/// <param name="s"></param>
		/// <param name="millisecondes"></param>
		/// <param name="endPoint"></param>
		/// <param name="message"></param>
		/// <returns>vrai si un message a été recu</returns>
		public static bool RecevoirMessageAvecDélai(this UdpClient s, int millisecondes, ref IPEndPoint endPoint, out string message, out bool délaiExpiré)
		{
			délaiExpiré = false;
			try
			{

				// option 1 : avec la méthode async et wait sur la tâche
				// La tâche ne s'annule pas donc malgré le timeout, un problème persistait car la tâche
				// récupérait quand même la donnée du buffer
				Task<UdpReceiveResult> t = s.ReceiveAsync();
				t.Wait(millisecondes);
				if (!t.IsCompleted)
					throw new TimeoutException("Trop long");


				// option 2 : avec un timeout 
				//https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.receivetimeout?f1url=https%3A%2F%2Fmsdn.microsoft.com%2Fquery%2Fdev15.query%3FappId%3DDev15IDEF1%26l%3DEN-US%26k%3Dk(System.Net.Sockets.Socket.ReceiveTimeout);k(DevLang-csharp)%26rd%3Dtrue&view=netframework-4.7.2
				// pour utiliser avec un timeout, spécifiez vous même un timeout en changeant la propriété Client.ReceiveTimeout = millisecondes
				// Ensuite, utilisez une méthode synchrone.



				byte[] données = t.Result.Buffer;
				endPoint = t.Result.RemoteEndPoint;
				message = Encoding.ASCII.GetString(données);

				return true;
			}
			catch (AggregateException aex)
			{
				// Si on ferme le serveur local PENDANT que la tâche est démarrée
				if (aex.InnerException is SocketException sex && sex.SocketErrorCode == SocketError.ConnectionReset)
				{
					Error.WriteLine("Erreur de réception UDP");



				}
			}
			catch (SocketException ex)
			{
				// Probablement un code 10054 
				Error.WriteLine("Erreur de socket au moment de la réception UDP.\n" +
																"Code Winsock : " + ex.SocketErrorCode);
			}
			catch (ObjectDisposedException ex)
			{
				Error.WriteLine("Erreur de socket au moment de la réception,\n" +
																"le socket a été fermé.\n" +
																ex.Message);
			}
			catch (TimeoutException)
			{
				délaiExpiré = true;
				Error.WriteLine("Le délai de réception est expiré.");
			}
			message = null;
			return false;
		}

		/// <summary>
		/// Encapsulation ultra-digérée des étapes nécessaires pour faire un serveur concurrent.
		/// 1 création du socket qui deviendra le socket maitre
		/// 2 bind sur toutes les interfaces (c'est normalement ce qu'on veut dans 99% des cas)
		/// 3 listen (activation de la file d'attente)
		/// 4 ajout du socket maitre dans une liste de tous les sockets (évidemment, il y en aura qu'un pour le moment)
		/// </summary>
		/// <param name="portÉcoute"></param>
		/// <param name="socketMaitre">le socket servant de guichet pour recevoir les nouveaux clients (analogie avec attribution d'un numéro au CLSC ou à la SAAQ)</param>
		/// <param name="listeComplèteSockets">la liste contenant le socket maitre</param>
		/// <returns>vrai si tout c'est bien déroulé</returns>
		public static bool PréparerServeurConcurrent(int portÉcoute, out Socket socketMaitre, out List<Socket> listeComplèteSockets)
		{

			try
			{
				socketMaitre = ÉcouterTCP(portÉcoute);
			}
			catch (Exception ex)
			{
				Error.WriteLine("Impossible de configurer le socket maitre.");
				Error.WriteLine(ex.ToString());
				socketMaitre = null; // l'initialisation est pour satisfaire le compilateur qui ne veut pas retourner sinon
				listeComplèteSockets = null;
				return false;
			}

			listeComplèteSockets = new List<Socket>(); // création du "guichet"
			listeComplèteSockets.Add(socketMaitre);

			return true;

		}
	}
}
