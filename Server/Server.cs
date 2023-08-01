using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ServerConsole
{
    internal class Server
    {
        private Socket serverSck;
        private int port;
        private string iPString;
        private Random random = new Random();
        private int numThreads = 0;
        private ConcurrentQueue<GomokuGame> games = new ConcurrentQueue<GomokuGame>();
        private ConcurrentDictionary<string, string> players = new ConcurrentDictionary<string, string>();
        private ConcurrentDictionary<string, GomokuGame> playingGames = new ConcurrentDictionary<string, GomokuGame>();

        //create a server constructor which can be used on the console program.cs - takes in ipAddress and port number
        public Server(string iPString, int port)
        {
            this.port = port;
            this.iPString = iPString;
            IPAddress iPAddress = IPAddress.Parse(iPString);
            serverSck = new Socket(iPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        }

        // method that initiates bind and listen to incoming connections 
        public void Start()
        {
            serverSck.Bind(new IPEndPoint(IPAddress.Parse(iPString), port));
            serverSck.Listen(0); // can accept multiple connections 
            Console.WriteLine("Listening at " + iPString + ":" + port);

            // always monitoring for incoming connections from multiple player 
            while (true)
            {
                // for every player socket that is connected to the server socket, a thread will be initiated and HandlePlayer method will be called 
                Socket playerSck = serverSck.Accept();
                Console.WriteLine("Connection established with " + playerSck.RemoteEndPoint);
                int threadID = Interlocked.Increment(ref numThreads); // keep a count of the number of thread in the server - prevent threads having same id number was already existing threads in system
                Thread playerThread = new Thread(() => HandlePlayer(playerSck));
                playerThread.Name = "" + threadID; // declared a name of the thread which can be later used to identify the thread 
                playerThread.Start();
            }
        }

        private void HandlePlayer(Socket playerSck)
        {
            // always running to handle player request and make appropriate method called depending on the received bytes
            while (true)
            {
                byte[] buffer = new byte[playerSck.ReceiveBufferSize];
                string receivedStr = "";
                while (!receivedStr.Contains("\r\n"))
                {
                    int bytesRead = playerSck.Receive(buffer);
                    receivedStr += Encoding.Default.GetString(buffer, 0, bytesRead); // append to string if receive from player is not complete, i.e. contains \r\n which indicated the end of string
                }
                string headerRequest = receivedStr.Split("\r\n")[0];
                string methodRequest = headerRequest.Split(" ")[0];
                string endpointRequest = headerRequest.Split(" ")[1];
                // handle the endpoints received based on what they begin with given that the method type is a GET method
                if (methodRequest.Equals("GET"))
                {
                    if (endpointRequest.StartsWith("/register"))
                    {
                        Register(playerSck);
                    }
                    else if (endpointRequest.StartsWith("/quit"))
                    {
                        Terminate(playerSck, endpointRequest);
                    }
                    else if (endpointRequest.StartsWith("/pairme"))
                    {
                        MatchPlayers(playerSck, endpointRequest);
                    }
                    else if (endpointRequest.StartsWith("/theirmove"))
                    {
                        HandleOppoentMove(playerSck, endpointRequest);
                    }
                    else if (endpointRequest.StartsWith("/mymove"))
                    {
                        HandlePlayerMove(playerSck, endpointRequest);
                    }
                }
            }
        }

        private void Register(Socket playerSck)
        {
            // note: /register endpoint
            string username = GenerateUsername();
            players[username] = Thread.CurrentThread.Name;

            byte[] responseBytes = Encoding.Default.GetBytes(username);
            /**
             * Below header code adapted from https://stackoverflow.com/questions/41076782/chrome-closes-tcp-connection-after-response and https://stackoverflow.com/questions/4015324/send-http-post-request-in-net for constucting an http packet that is sent to other connections 
             */
            string header = $"HTTP/1.1 200 OK\r\n" +
                $"Connection: keep-alive\r\n" +
                $"Access-Control-Allow-Origin: *\r\n" +
                $"Access-Control-Allow-Methods: GET, POST\r\n" +
                $"Access-Control-Allow-Headers: Content-Type\r\n" +
                $"Content-Type: text/plain\r\n" +
                $"Content-Length: {responseBytes.Length}\r\n\r\n";
            playerSck.Send(Encoding.Default.GetBytes(header));
            playerSck.Send(responseBytes);
            // display on console
            Console.WriteLine("Thread " + Thread.CurrentThread.Name + " sent response to " + playerSck.RemoteEndPoint + " for /register");
        }

        // match player so to initiate a new gomoku game
        private void MatchPlayers(Socket playerSck, string endpointRequest)
        {
            // note: /pairme?player={username}
            string username = endpointRequest.Split("=")[1];
            // if the player that requested for matching does not exist in the system then respond accordingly
            if (!players.ContainsKey(username))
            {
                string badReqStr = "HTTP/1.1 400 Bad Request\r\n\r\n";
                byte[] strBytes = Encoding.Default.GetBytes(badReqStr);
                playerSck.Send(strBytes);
                return;
            }
            GomokuGame newGame;
            foreach (GomokuGame game in playingGames.Values)
            {
                if (game.playerOne.Equals(username) || game.playerTwo.Equals(username))
                {
                    // initiate a new game if username mathches and instantiate the newGame variable to fill some data before sending between player sockets 
                    newGame = new GomokuGame
                    {
                        iD = game.iD,
                        playerOne = game.playerOne,
                        playerTwo = game.playerTwo,
                        state = "progressing"
                    };
                    SendRecord(playerSck, newGame);
                    return;
                }
            }
            // if new game removed and return to beginning of queue then:
            if (games.TryDequeue(out newGame))
            {
                if (!username.Equals(newGame.playerOne))
                {
                    newGame.playerTwo = username;
                    playingGames[newGame.iD] = newGame;
                    newGame.state = "progressing";
                }
                else
                {
                    games.Enqueue(newGame);
                }
            }
            else
            {
                newGame = new GomokuGame
                {
                    iD = Guid.NewGuid().ToString(),
                    playerOne = username,
                    state = "waiting"
                };
                games.Enqueue(newGame);
            }
            SendRecord(playerSck, newGame);
            Console.WriteLine("Thread " + Thread.CurrentThread.Name + " sent response to " + playerSck.RemoteEndPoint + " for " + endpointRequest);
        }

        // method to handle if a user wishes to quit the game
        private void Terminate(Socket playerSck, string endpointRequest)
        {
            // note: /quit?player={username}&id={gameId}
            string[] parameter = endpointRequest.Split("?")[1].Split("&");
            string username = parameter[0].Split("=")[1].Trim();
            string gameId = parameter[1].Split("=")[1].Trim();
            string header = "";

            if (!players.ContainsKey(username) || !playingGames.ContainsKey(gameId)) // if game does not exist or player does not exist
            {
                header = GenerateDefaultHeader("400 Bad Request") + $"Content-Length: {0}\r\n\r\n";
            }
            else if (!playingGames.ContainsKey(gameId) && players.ContainsKey(username)) // if game does not exist but player exists - this is usually when one player had ended the game and removed gameId from system 
            {
                players.TryRemove(username, out _); // removes player from system
            }
            else // if game exist and player exist
            {
                GomokuGame game = playingGames[gameId];
                if (!game.playerOne.Equals(username) && !game.playerTwo.Equals(username)) // if player does not belong to the gameid specified - could be malicous users changing the header
                {
                    header = GenerateDefaultHeader("403 Forbidden") + $"Content-Length: {0}\r\n\r\n";
                }
                else
                {
                    if (playingGames.TryRemove(gameId, out _))
                    {
                        header = GenerateDefaultHeader("200 OK") + $"Content-Length: {0}\r\n\r\n";
                        players.TryRemove(username, out _);
                        Console.WriteLine("Thread " + Thread.CurrentThread.Name + " closing connection with " + playerSck.RemoteEndPoint + " and terminating");
                    }
                    else
                    {
                        header = GenerateDefaultHeader("500 Internal Server Error") + $"Content-Length: {0}\r\n\r\n";
                    }
                }
            }
            playerSck.Send(Encoding.Default.GetBytes(header));
        }

        private void HandlePlayerMove(Socket playerSck, string endpointRequest)
        {
            // note: /mymove?player={username}&id={gameId}&move={move}
            string[] parameter = endpointRequest.Split("?")[1].Split("&");
            string username = parameter[0].Split("=")[1].Trim();
            string gameId = parameter[1].Split("=")[1].Trim();
            string move = parameter[2].Split("=")[1].Trim();
            string header;

            if (!players.ContainsKey(username) || !playingGames.ContainsKey(gameId)) // if player does not exist in system
            {
                header = GenerateDefaultHeader("400 Bad Request") + $"Content-Length: {0}\r\n\r\n";
            }
            else
            {
                GomokuGame game = playingGames[gameId];
                if (!game.playerOne.Equals(username) && !game.playerTwo.Equals(username)) // if player does not exist for the specified game id 
                {
                    header = GenerateDefaultHeader("403 Forbidden") + $"Content-Length: {0}\r\n\r\n";
                }
                else if (!game.state.Equals("progressing")) // if the game is still waiting for pairing 
                {
                    header = GenerateDefaultHeader("409 Conflict") + $"Content-Length: {0}\r\n\r\n";
                }
                else
                {
                    header = GenerateDefaultHeader("200 OK") + $"Content-Length: {0}\r\n\r\n";
                    // store player move depending on username
                    if (game.playerOne.Equals(username))
                    {
                        game.playerOneMove = move;
                    }
                    else
                    {
                        game.playerTwoMove = move;
                    }
                }
            }
            playerSck.Send(Encoding.Default.GetBytes(header));
            Console.WriteLine("Thread " + Thread.CurrentThread.Name + " sent response to " + playerSck.RemoteEndPoint + " for " + endpointRequest);
        }

        private void HandleOppoentMove(Socket playerSck, string endpointRequest)
        {
            // note: /theirmove?player={username}&id={gameId}
            GomokuGame game = null;
            string[] parameter = endpointRequest.Split("?")[1].Split("&");
            string username = parameter[0].Split("=")[1].Trim();
            string gameId = parameter[1].Split("=")[1].Trim();
            string header = "";
            string responsBody = "";
            if (!(!players.ContainsKey(username) || !playingGames.ContainsKey(gameId)))
            {
                game = playingGames[gameId];
                if (game.playerOne != username && game.playerTwo != username) // if player does not exist for the specified game id 
                {
                    header = GenerateDefaultHeader("403 Forbidden") + $"Content-Length: {0}\r\n\r\n";
                }
                else if (!game.state.Equals("progressing")) // if game is still waiting to match players 
                {
                    header = GenerateDefaultHeader("409 Conflict") + $"Content-Length: {0}\r\n\r\n";
                }
                else
                {
                    if (!((game.playerOne.Equals(username) && game.playerTwoMove == null) || (game.playerTwo.Equals(username) && game.playerOneMove == null)))
                    {
                        responsBody = game.playerOne == username ? game.playerTwoMove : game.playerOneMove;
                    }
                    header = GenerateDefaultHeader("200 OK") + $"Content-Length: {responsBody.Length}\r\n\r\n";
                }
            }
            playerSck.Send(Encoding.Default.GetBytes(header));
            playerSck.Send(Encoding.Default.GetBytes(responsBody));
            Console.WriteLine("Thread " + Thread.CurrentThread.Name + " sent response to " + playerSck.RemoteEndPoint + " for " + endpointRequest);
            if (game != null)
            {
                if (game.playerOne.Equals(username)) // clearing player move - this is to prevent duplicate moves or scrambling of game experience 
                {
                    game.playerOneMove = null;
                }
                else
                {
                    game.playerTwoMove = null;
                }
            }

        }

        private void SendRecord(Socket playerSck, GomokuGame game)
        {
            //constructing a json object which is ideal for sending data for game 
            var gameJSON = new
            {
                id = game.iD,
                playerOne = game.playerOne,
                playerOneMove = game.playerOneMove,
                playerTwo = game.playerTwo,
                playerTwoMove = game.playerTwoMove,
                state = game.state
            };
            string gameJSONStr = JsonSerializer.Serialize(gameJSON); // make tostring of the gameJSON 
            byte[] responseBytes = Encoding.Default.GetBytes(gameJSONStr);
            //constructing an http packet that can be sent along with response
            string header = GenerateDefaultHeader("200 OK") +
                $"Content-Type: application/json\r\n" +
                $"Content-Length: {responseBytes.Length}\r\n\r\n"; 
            playerSck.Send(Encoding.Default.GetBytes(header));
            playerSck.Send(responseBytes);
        }

        private string GenerateDefaultHeader(string status)
        {
            // http packet that is used to handle 
            return $"HTTP/1.1 {status}\r\n" +
                   $"Access-Control-Allow-Origin: *\r\n" +
                   $"Access-Control-Allow-Methods: GET, POST\r\n" +
                   $"Access-Control-Allow-Headers: Content-Type\r\n";
        }

        private string GenerateUsername()
        {
            // two string arrays used for generating username 
            string[] nounArray = { "Penalty", "Description", "Organisation", "Committee", "Responsiblity", "Departure", "Community", "Failure", "Extent", "Garbage", "Member", "Investment", "Sir", "Sample", "Tension", "Judgment", "Tennis", "Year", "Thanks", "Proposal", "Presentation", "Story", "Tooth", "Assistance", "Singer", "Area", "Agreement", "Owner", "Trainer", "Physics" };

            string[] adjArray = { "Fair", "Gabby", "Sufficient", "Mean", "Rotten", "Numerous", "Rare", "Tranquil", "Defiant", "Old", "Hellish", "Electrical", "Forgetful", "Sticky", "Staking", "Zippy", "Strong", "Abaft", "Full", "Steadfast", "Alcoholic", "Meek", "Meaty", "Grotesque", "Tacit", "Unknown", "Numberless", "Nauseating", "Wanting", "Astonishing" };

            string username = "";
            do
            {
                username = adjArray[random.Next(adjArray.Length)] + nounArray[random.Next(adjArray.Length)];
            } while (!players.TryAdd(username, Thread.CurrentThread.Name)); // to prevent players from having the same username

            return username;
        }
    }
}
