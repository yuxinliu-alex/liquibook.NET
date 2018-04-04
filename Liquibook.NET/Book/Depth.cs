﻿using System;
using System.Collections.Generic;
using System.Linq;
using Liquibook.NET.Types;

namespace Liquibook.NET.Book
{
    public class Depth
    {
        private const int _size = 5;
        private int _lastChange = 0;
        private int _lastPublishedChange = 0;
        private Quantity _ignoreBidFillQuantity = 0;
        private Quantity _ignoreAskFillQuantity = 0;
        private readonly SortedDictionary<Price, DepthLevel> _excessBidLevels = new SortedDictionary<Price, DepthLevel>();
        private readonly SortedDictionary<Price, DepthLevel> _excessAskLevels =
            new SortedDictionary<Price, DepthLevel>(Comparer<Price>.Create((x, y) => y.CompareTo(x)));
        private readonly SortedDictionary<Price, DepthLevel> _bidLevels = new SortedDictionary<Price, DepthLevel>();
        private readonly SortedDictionary<Price, DepthLevel> _askLevels =
            new SortedDictionary<Price, DepthLevel>(Comparer<Price>.Create((x, y) => y.CompareTo(x)));

        public bool Changed => _lastChange > _lastPublishedChange;
        
        public void AddOrder(Price price, Quantity quantity, bool isBid)
        {
            var level = FindLevel(price, isBid, false);
            if (level != null)
            {
                level.AddOrder(quantity);
                if (!level.IsExcess)
                {
                    _lastChange += 1;
                    level.LastChange = _lastChange;
                }
            }
        }

        public void IgnoreFillQuantity(Quantity quantity, bool isBid)
        {
            if (isBid)
            {
                if (_ignoreBidFillQuantity != 0)
                {
                    throw new Exception("Unexpected ignore bid fill quantity"); //TODO errors?
                }

                _ignoreBidFillQuantity = quantity;
            }
            else
            {
                if (_ignoreAskFillQuantity != 0)
                {
                    throw new Exception("Unexpected ignore ask fill quantity"); //TODO errors?
                }

                _ignoreAskFillQuantity = quantity;
            }
        }

        public void FillOrder(Price price, Quantity fillQty, bool filled, bool isBid)
        {
            if (isBid && _ignoreBidFillQuantity != 0)
            {
                _ignoreBidFillQuantity -= fillQty;
            }
            else if(!isBid && _ignoreAskFillQuantity != 0)
            {
                _ignoreAskFillQuantity -= fillQty;
            }
            else if (filled)
            {
                CloseOrder(price, fillQty, isBid);
            }
            else
            {
                ChangeOrderQuantity(price, -fillQty, isBid);
            }
        }

        public bool CloseOrder(Price price, Quantity openQuantity, bool isBid)
        {
            var level = FindLevel(price, isBid, false);
            if (level != null)
            {
                if (level.CloseOrder(openQuantity))
                {
                    EraseLevel(level, isBid);
                    return true;
                }
                else
                {
                    level.LastChange = ++_lastChange;
                }
            }

            return false;
        }

        public void ChangeOrderQuantity(Price price, Quantity quantityDelta, bool isBid)
        {
            var level = FindLevel(price, isBid, false);
            if (level != null && quantityDelta != 0)
            {
                if (quantityDelta >0)
                {
                    level.IncreaseQty(quantityDelta);
                }
                else
                {
                    level.DecreaseQty(Math.Abs(quantityDelta));
                }

                level.LastChange = ++_lastChange;
            }
        }

        public bool ReplaceOrder(int currentPrice, int newPrice, Quantity currentQuantity, Quantity newQuantity, bool isBid)
        {
            var erased = false;
            if (currentPrice == newPrice)
            {
                var quantityDelta = newQuantity - currentQuantity;
                ChangeOrderQuantity(currentPrice, quantityDelta, isBid);
            }
            else
            {
                AddOrder(newPrice, newQuantity, isBid);
                erased = CloseOrder(currentPrice, currentQuantity, isBid);
            }

            return erased;
        }

        public bool NeedsBidRestoration(int restorationPrice)
        {
            throw new NotImplementedException(); // TODO dont get this??
        }

        public bool NeedsAskRestoration(Price price)
        {
            throw new NotImplementedException(); // TODO dont get this??
        }

        public DepthLevel FindLevel(Price price, bool isBid, bool shouldCreate)
        {
            DepthLevel result;
            var levels = isBid ? _bidLevels : _askLevels;
            
            if (levels.TryGetValue(price, out result))
            {
                return result;
            }

            if (shouldCreate && levels.Count < _size)
            {
                result = new DepthLevel(price, false);
                levels.Add(price, result);
                return result;
            }

            if (shouldCreate)
            {
                foreach (KeyValuePair<Price, DepthLevel> x in levels)
                {
                    if (isBid && x.Key < price)
                    {
                        InsertLevelBefore(x.Value, true, price);
                        break;
                    }

                    if (!isBid && x.Key > price)
                    {
                        InsertLevelBefore(x.Value, false, price);
                    }
                    
                }
            }

            var lastLevelPrice = LastLevel(levels);
            if (isBid && price < lastLevelPrice)
            {
                // add to excess bid levels
                if (_excessBidLevels.TryGetValue(price, out var x))
                {
                    result = x;
                }
                else if(shouldCreate)
                {
                    var newDepthLevel = new DepthLevel(price, true);
                    _excessBidLevels.Add(price, newDepthLevel);
                    result = newDepthLevel;
                }
            }

            if (!isBid && price > lastLevelPrice)
            {
                // add to excess ask levels
                if (_excessAskLevels.TryGetValue(price, out var x))
                {
                    result = x;
                }
                else if (shouldCreate)
                {
                    var newDepthLevel = new DepthLevel(price, true);
                    _excessAskLevels.Add(price, newDepthLevel);
                    result = newDepthLevel;
                }
            }

            return result;
        }

        private void InsertLevelBefore(DepthLevel level, bool isBid, Price price)
        {
            var levels = isBid ? _bidLevels : _askLevels;
            var excessLevels = isBid ? _excessBidLevels : _excessAskLevels;
            var lastLevelPrice = LastLevel(levels);
            
            ++_lastChange;
            level.LastChange = _lastChange;
            levels.Add(price, level);
            foreach (KeyValuePair<Price,DepthLevel> depthLevel in levels)
            {
                if (isBid && price > depthLevel.Value.Price)
                {
                    depthLevel.Value.LastChange = _lastChange;
                }

                if (!isBid && price < depthLevel.Value.Price)
                {
                    depthLevel.Value.LastChange = _lastChange;
                }
            }
            
            if (levels.Count > _size)
            {
                var droppedOutLevel = levels[lastLevelPrice];
                var newExcessLevel = new DepthLevel(lastLevelPrice, true);
                newExcessLevel.Set(lastLevelPrice, droppedOutLevel.AggregateQty, droppedOutLevel.OrderCount, _lastChange);
                levels.Remove(lastLevelPrice);
                excessLevels.Add(lastLevelPrice, newExcessLevel);
            }
        }

        public void EraseLevel(DepthLevel level, bool isBid)
        {
            if (level.IsExcess)
            {
                if (isBid)
                {
                    _excessBidLevels.Remove(level.Price);
                }
                else
                {
                    _excessAskLevels.Remove(level.Price);
                }
            }
            else
            {
                ++_lastChange;
                var levels = isBid ? _bidLevels : _askLevels;
                var excessLevels = isBid ? _excessBidLevels : _excessAskLevels;

                foreach (KeyValuePair<Price,DepthLevel> depthLevel in levels)
                {
                    if (isBid && level.Price > depthLevel.Value.Price)
                    {
                        depthLevel.Value.LastChange = _lastChange;
                    }

                    if (!isBid && level.Price < depthLevel.Value.Price)
                    {
                        depthLevel.Value.LastChange = _lastChange;
                    }
                }

                levels.Remove(level.Price);
                
                if(excessLevels.Count == 0) return;
                var replacementLevel = excessLevels.First().Value;
                replacementLevel.LastChange = _lastChange;
                levels[replacementLevel.Price] = replacementLevel;
            }
        }

        private static int LastLevel(SortedDictionary<Price, DepthLevel> levels)
        {
            var listLength = levels.Count;
            return levels.ElementAt(listLength).Key;
        }

        public void Published()
        {
            _lastPublishedChange = _lastChange;
        }
    }
}