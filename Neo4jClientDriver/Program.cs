using Neo4jClient;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace Neo4jClientDriver
{
    class Program
    {
        public class TreeEdge
        {
            public long Id { get; set; }
        }

        public class Relationship : TreeEdge
        {
            public string Name { get; set; }
        }

        public class TreeNodeDataEntity
        {
            [JsonIgnore]
            public long Id { get; set; }
        }

        public class TreeNode<TreeNodeData> where TreeNodeData : TreeNodeDataEntity
        {
            public TreeNodeData Data { get; set; }
            public TreeNode<TreeNodeData> Parent { get; set; }
            public List<TreeNode<TreeNodeData>> Children { get; set; }

            public int GetHeight()
            {
                int height = 1;
                TreeNode<TreeNodeData> current = this;
                while (current.Parent != null)
                {
                    height++;
                    current = current.Parent;
                }
                return height;
            }

            public string GetRelationshipId(TreeNode<TreeNodeData> anotherNode)
            {
                return $"_{this.Data.Id}_TO_{anotherNode.Data.Id}_";
            }
        }

        public class Tree<TreeNodeData> where TreeNodeData : TreeNodeDataEntity
        {
            public TreeNode<TreeNodeData> Root { get; set; }
        }

        public class Person : TreeNodeDataEntity
        {
            public string Name { get; set; }
            public string Role { get; set; }

            public Person() { }
            public Person(string name, string role)
            {
                Name = name;
                Role = role;
            }
        }

        public class Decision//Node
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public string Text { get; set; }
            public string Action { get; set; }
            public List<Option> Options { get; set; }//Nullable if is a Leaf
        }

        public class Option//Relationship
        {
            public string Title { get; set; }
            public string Text { get; set; }
            public string Relationship { get; set; }//Any Object
            public Decision Decision { get; set; }
        }

        public class Neo4jService
        {
            private GraphClient _client;
            public Neo4jService()
            {
                ConnectDatabase();
                ClearDatabase();
            }

            private void ConnectDatabase()
            {
                _client = new GraphClient(new Uri("http://localhost:7474"), "neo4j", "sullevan");
                _client.ConnectAsync().Wait();
            }

            public void ClearDatabase()
            {
                _client.Cypher
                    .Match("(n)")
                    .OptionalMatch("(n)-[r]-()")
                    .Delete("n,r")
                    .ExecuteWithoutResultsAsync()
                    .Wait();
            }

            public async Task CreateTree(Tree<Person> tree)
            {
                await CreateTreeNode(tree.Root);
            }

            public async Task CreateTreeNode(TreeNode<Person> treeNode)
            {
                if (treeNode == null)
                {
                    return;
                }

                await CreateNode(treeNode);

                await CreateRelationshipWithParent(treeNode);

                if (treeNode.Children == null)
                {
                    return;
                }
                foreach (TreeNode<Person> node in treeNode.Children)
                {
                    await CreateTreeNode(node);
                }
                return;
            }

            public async Task CreateTreeNode(TreeNode<Person> treeNode, long parentNodeId)
            {
                var treeNodeParent = new TreeNode<Person>();
                treeNodeParent.Data = await GetNode(parentNodeId);
                treeNode.Parent = treeNodeParent;
                await CreateTreeNode(treeNode);
            }

            public async Task<long> CreateNode<TreeNodeData>(TreeNode<TreeNodeData> treeNode) where TreeNodeData : TreeNodeDataEntity
            {
                if (treeNode == null)
                {
                    return 0;
                }

                var json = JsonSerializer.Serialize(treeNode.Data).AsQueryable();
                var cypherPropesties = CypherSerializeToCreatePropertiesObject(treeNode.Data);

                string cypherQuery = "(j:" + nameof(treeNode.Data) + cypherPropesties + ")";

                long createdId = (await _client.Cypher
                    .Create(cypherQuery)
                    .Return((j) => j.Id()
                    ).ResultsAsync).FirstOrDefault();

                treeNode.Data.Id = createdId;
                return createdId;
            }

            public string CypherSerializeToCreatePropertiesObject(object objectToSerialize)
            {
                var queryableChar = JsonSerializer.Serialize(objectToSerialize)
                    .AsQueryable();
                bool remove = true;
                StringBuilder cypherProperties = new StringBuilder();
                foreach(var character in queryableChar)
                {
                    if (character.Equals(':'))
                    {
                        remove = false;
                    }
                    if (character.Equals(','))
                    {
                        remove = true;
                    }
                    if (character.Equals('"') && remove)
                    {
                        continue;
                    }
                    cypherProperties.Append(character);
                }
                return cypherProperties.ToString();
            }

            public string CypherSerializeToSetPropertiesQuery(string node, object objectToSerialize)
            {
                var queryableChar = JsonSerializer.Serialize(objectToSerialize)
                    .AsQueryable();
                bool remove = true;
                StringBuilder cypherProperties = new StringBuilder();
                node += ".";
                cypherProperties.Append(node);
                foreach (var character in queryableChar)
                {
                    if (character.Equals('{') || character.Equals('}'))
                    {
                        continue;
                    }
                    if (character.Equals(':'))
                    {
                        remove = false;
                        cypherProperties.Append('=');
                        continue;
                    }
                    if (character.Equals(','))
                    {
                        remove = true;
                        cypherProperties.Append(character);
                        cypherProperties.Append(node);
                        continue;
                    }
                    if (character.Equals('"') && remove)
                    {
                        continue;
                    }
                    cypherProperties.Append(character);
                }
                return cypherProperties.ToString();
            }

            public async Task UpdateNode<TreeNodeData>(TreeNode<TreeNodeData> treeNode, long nodeId) where TreeNodeData : TreeNodeDataEntity
            {
                if (treeNode == null || treeNode.Data == null)
                {
                    return;
                }
                var setQuery = CypherSerializeToSetPropertiesQuery("node", treeNode.Data);
                await _client.Cypher
                    .Match("(node)")
                    .Where("id(node) = " + nodeId.ToString())
                    .Set(setQuery)
                    .ExecuteWithoutResultsAsync();
            }

            public async Task DeleteNode(long nodeId)
            {
                await _client.Cypher
                    .Match("(n)")
                    .Where("id(n) = " + nodeId.ToString())
                    .DetachDelete("n")
                    .ExecuteWithoutResultsAsync();
            }

            public async Task CreateRelationshipWithParent<TreeNodeData>(TreeNode<TreeNodeData> treeNode) where TreeNodeData : TreeNodeDataEntity
            {
                if (treeNode != null && treeNode.Parent != null)
                {
                    await CreateRelationship(treeNode.Parent, treeNode);
                }
            }

            public async Task<long> CreateRelationship<TreeNodeData>(TreeNode<TreeNodeData> treeNodeA, TreeNode<TreeNodeData> treeNodeB) where TreeNodeData : TreeNodeDataEntity
            {
                if (treeNodeA != null && treeNodeA.Data != null && treeNodeB != null && treeNodeB.Data != null)
                {
                    return (await _client.Cypher
                        .Match("(nodeA)")
                        .Where("id(nodeA) = "+ treeNodeA.Data.Id)
                        .Match("(nodeB)")
                        .Where("id(nodeB) = "+ treeNodeB.Data.Id)
                        .Merge("(nodeA)-[r:" + treeNodeA.GetRelationshipId(treeNodeB) + " {Name: '" + treeNodeA.GetRelationshipId(treeNodeB) + "'}]->(nodeB)")
                        .Return((r) => r.Id())
                        .ResultsAsync).FirstOrDefault();
                }
                return 0;
            }

            public async Task<IEnumerable<Relationship>> GetNodeRelationships(long nodeId)
            {
                IEnumerable<Relationship> nodeRelationships = await _client.Cypher
                    .Match("(n)-[r]->()")
                    .Where("id(n) = " + nodeId.ToString())
                    .Return((r) => r.As<Relationship>()).ResultsAsync;

                return nodeRelationships;
            }

            public async Task<Relationship> GetRelationship(long relationshipId)
            {
                var relationship = (await _client.Cypher
                    .Match("()-[r]-()")
                    .Where("id(r) = " + relationshipId.ToString())
                    .Return((r) => r.As<Relationship>()).ResultsAsync).FirstOrDefault();
                if (relationship != null)
                {
                    relationship.Id = relationshipId;
                }
                return relationship;
            }

            public async Task DeleteRelationship(long relationshipId)
            {
                await _client.Cypher
                    .Match("()-[r]-()")
                    .Where("id(r) = " + relationshipId.ToString())
                    .Delete("r")
                    .ExecuteWithoutResultsAsync();
            }

            public async Task UpdateRelationship(Relationship relationship, long relationshipId)
            {
                if (relationship != null)
                {
                    await _client.Cypher
                        .Match("()-[r]-()")
                        .Where("id(r) = " + relationshipId.ToString())
                        .Set("r." + nameof(Relationship.Name) + "='" + relationship.Name + "'")
                        .ExecuteWithoutResultsAsync();
                }
            }

            public async Task<Person> GetNextNode(long relationshipId)
            {
                Person nextNode = (await _client.Cypher.Match("()-[r]->(n)")
                    .Where("id(r) = " + relationshipId.ToString())
                    .Return<Person>("n").ResultsAsync).FirstOrDefault();
                return nextNode;
            }

            public async Task<Person> GetNode(long nodeId)
            {
                Person node = (await _client.Cypher.Match("(n)")
                    .Where("id(n) = " + nodeId.ToString())
                    .Return<Person>("n").ResultsAsync).FirstOrDefault();
                if (node != null)
                {
                    node.Id = nodeId;
                }
                return node;
            }

        }
        static async Task Main(string[] args)
        {
            Tree<Person> company = new Tree<Person>();
            company.Root = new TreeNode<Person>()
            {
                Data = new Person("Marcin Jamro", "CEO"),
                Parent = null
            };

            company.Root.Children = new List<TreeNode<Person>>()
            {
                new TreeNode<Person>()
                {
                    Data = new Person("John Smith", "Head of Development"),
                    Parent = company.Root
                },
                new TreeNode<Person>()
                {
                    Data = new Person("Mary Fox", "Head of Research"),
                    Parent = company.Root
                },
                new TreeNode<Person>()
                {
                    Data = new Person("Lily Smith", "Head of Sales"),
                    Parent = company.Root
                }
            };

            company.Root.Children[0].Children = new List<TreeNode<Person>>()
            {
                new TreeNode<Person>()
                {
                    Data = new Person("Chris Morris", "Senior Developer"),
                    Parent = company.Root.Children[0]
                }
            };

            company.Root.Children[1].Children = new List<TreeNode<Person>>()
            {
                new TreeNode<Person>()
                {
                    Data = new Person("Jimmy Stewart", "Senior Researcher"),
                    Parent = company.Root.Children[1]
                },
                new TreeNode<Person>()
                {
                    Data = new Person("Andy Wood", "Senior Researcher"),
                    Parent = company.Root.Children[1]
                }
            };

            company.Root.Children[2].Children = new List<TreeNode<Person>>()
            {
                new TreeNode<Person>()
                {
                    Data = new Person("Anthony Black", "Senior Sales Specialist"),
                    Parent = company.Root.Children[2]
                },
                new TreeNode<Person>()
                {
                    Data = new Person("Angela Evans", "Senior Sales Specialist"),
                    Parent = company.Root.Children[2]
                },
                new TreeNode<Person>()
                {
                    Data = new Person("Tommy Butler", "Senior Account Manager"),
                    Parent = company.Root.Children[2]
                }
            };

            company.Root.Children[0].Children[0].Children = new List<TreeNode<Person>>()
            {
                new TreeNode<Person>()
                {
                    Data = new Person("Eric Green", "Junior Developer"),
                    Parent = company.Root.Children[0].Children[0]
                },
                new TreeNode<Person>()
                {
                    Data = new Person("Ashley Lopez", "Junior Developer"),
                    Parent = company.Root.Children[0].Children[0]
                }
            };

            company.Root.Children[0].Children[0].Children[1].Children = new List<TreeNode<Person>>()
            {
                new TreeNode<Person>()
                {
                    Data = new Person("Emily Young", "Developer Intern"),
                    Parent = company.Root.Children[0].Children[0].Children[1]
                }
            };

            #region Demonstração

            var service = new Neo4jService();
            await service.CreateTree(company);

            var newTreeNode = new TreeNode<Person>()
            {
                Data = new Person("Emily Old", "Developer Extern"),
                Parent = null
            };

            var anotherTreeNode = new TreeNode<Person>()
            {
                Data = new Person("Emily Oldest", "Dev"),
                Parent = null
            };

            long relationshipId = 0;

            /*Inicio CRUD Node*/
            var nodeId = await service.CreateNode(newTreeNode);
            await service.UpdateNode(anotherTreeNode, nodeId);
            var node = await service.GetNode(nodeId);
            await service.DeleteNode(nodeId);
            /*Fim CRUD Node*/

            var nodeIdA = await service.CreateNode(newTreeNode);
            var nodeA = await service.GetNode(nodeIdA);
            var nodeIdB = await service.CreateNode(anotherTreeNode);
            var nodeB = await service.GetNode(nodeIdB);

            var relationship = new Relationship();
            relationship.Name = "Opção1";
            var anotherRelationship = new Relationship();
            anotherRelationship.Name = "Opção2";

            var treeNodeA = new TreeNode<Person>();
            treeNodeA.Data = nodeA;
            var treeNodeB = new TreeNode<Person>();
            treeNodeB.Data = nodeB;

            /*Inicio CRUD Relationship*/
            relationshipId = await service.CreateRelationship(treeNodeA, treeNodeB);
            await service.UpdateRelationship(relationship, relationshipId);
            relationship = await service.GetRelationship(relationshipId);
            await service.DeleteRelationship(relationshipId);
            /*Fim CRUD Relationship*/

            #endregion

        }
    }
}
